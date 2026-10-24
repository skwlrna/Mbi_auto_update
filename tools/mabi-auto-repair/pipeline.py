"""One-shot evidence-first MabiAutoRepair orchestration.

A single explicit command reads one trusted log-repository incident's summary,
recent.log and screen.png. Read-only image analysis precedes any workspace-write
attempt. Earlier-version reports may be diagnosed but never patched against a
newer version without a matching-version reproduction.

No polling, automatic retry, PR creation, merging, deploy or game input.
"""
from __future__ import annotations

import hashlib
import urllib.request
from pathlib import Path
from urllib.parse import quote

from image_probe import LOG_RAW, VALID_LOG, inspect_image

MAX_RECENT_LOG_BYTES = 64 * 1024
MAX_EXCERPT_CHARS = 5000


def _event_path(source: str) -> tuple[str, str]:
    if not (source.startswith("github:") or source.startswith("local:")):
        raise ValueError("Unsupported error origin: use sync --github or sync --dir")
    kind, path = source.split(":", 1)
    if not VALID_LOG.fullmatch(path):
        raise ValueError("Invalid runtime incident path")
    return kind, path


def _read_bounded(file: Path) -> bytes:
    if not file.is_file() or file.is_symlink() or file.stat().st_size > MAX_RECENT_LOG_BYTES:
        raise ValueError("Missing, symlinked or oversized recent.log")
    data = file.read_bytes()
    if len(data) > MAX_RECENT_LOG_BYTES:
        raise ValueError("recent.log exceeds size limit")
    return data


def recent_log(job: dict, log_root: Path | None = None) -> dict:
    """Retrieve recent.log from the same fixed incident directory, never a log URL.

    The caller gets a bounded *redacted* excerpt, not the raw log. Raw bytes are
    neither persisted nor included in the result shown to the operator.
    """
    kind, relative = _event_path(job["source"])
    rel_log = relative[:-len("summary.json")] + "recent.log"
    if kind == "github":
        if log_root is not None:
            raise ValueError("--log-root is only for locally imported incidents")
        request = urllib.request.Request(
            LOG_RAW + quote(rel_log, safe="/"),
            headers={"User-Agent": "MabiAutoRepair/0.4", "Accept": "text/plain"})
        with urllib.request.urlopen(request, timeout=20) as response:
            data = response.read(MAX_RECENT_LOG_BYTES + 1)
        if len(data) > MAX_RECENT_LOG_BYTES:
            raise ValueError("Remote recent.log exceeds size limit")
    else:
        if log_root is None or not log_root.is_dir():
            raise ValueError("--log-root required for local errors")
        base = log_root.resolve()
        path = log_root / rel_log
        if not path.resolve().is_relative_to(base):
            raise ValueError("Log path escapes selected root")
        data = _read_bounded(path)

    if not data.strip():
        raise ValueError("recent.log empty: no log evidence")
    # Tail events are usually closest to the actual abort. Keep no raw log on disk.
    from repair import sanitized
    tail = "\n".join(data.decode("utf-8-sig", errors="replace").splitlines()[-100:])
    # Sanitization removes absolute PC paths, URLs and typical secrets.
    excerpt = sanitized(tail[-MAX_EXCERPT_CHARS:])
    if not excerpt:
        raise ValueError("No usable recent.log details")
    return {"sha256": hashlib.sha256(data).hexdigest(), "bytes": len(data),
            "excerpt": excerpt}


def _local_png(job: dict, log_root: Path) -> Path:
    kind, relative = _event_path(job["source"])
    if kind != "local":
        raise ValueError("Local image requested for non-local incident")
    root = log_root.resolve()
    path = log_root / (relative[:-len("summary.json")] + "screen.png")
    if not path.resolve().is_relative_to(root):
        raise ValueError("Image path escapes selected log root")
    if not path.is_file() or path.is_symlink():
        raise ValueError("Missing or symlinked screenshot")
    return path


def investigate(state, key: str, repo: Path, log_root: Path | None,
                execute: bool, acknowledge_cost: bool,
                max_daily: int | None = None,
                retry_regression_once: bool = False) -> dict:
    # Import is delayed to avoid a cycle: repair.py owns the CLI and calls us.
    from repair import current_source_version, incident_preflight, run_one, suite_for

    if execute and not acknowledge_cost:
        raise ValueError("--acknowledge-cost is required for actual Codex calls")
    job = state.job(key)
    if execute and (job["status"] != "new" or job["calls"]):
        raise ValueError("This error already had a repair attempt; no automatic retries")
    kind, _ = _event_path(job["source"])
    if kind == "local" and log_root is None:
        raise ValueError("--log-root required for local error evidence")

    # Read and validate real evidence BEFORE paying for a model call.
    log = recent_log(job, log_root)
    local_image = _local_png(job, log_root) if kind == "local" else None
    picture = inspect_image(job, local_image, kind == "github",
                            execute, acknowledge_cost, log_excerpt=log["excerpt"])
    version = current_source_version(repo)
    before = incident_preflight(job, suite_for(job["mode"]), repo)
    result = {
        "id": key, "model": picture["model"], "reasoning_effort": picture["reasoning_effort"],
        "error_version": job["version"], "source_version": version,
        "image": picture["image"],
        "recent_log": {"sha256": log["sha256"], "bytes": log["bytes"]},
        "image_calls": picture["codex_calls"],
        "patch_calls_reserved": 0,
        "code_modified": False, "real_game_verified": False,
        "candidate_verified": False,
    }
    if not execute:
        result.update(status="evidence_ready", preflight_reasons=before["preflight_reasons"],
                      next="DRY_RUN: use --execute --acknowledge-cost to analyze; no AI calls")
        return result
    if picture["status"] != "image_analysis_returned":
        result.update(status="needs_review", reason="image_diagnostic_failed",
                      image_status=picture["status"])
        return result
    result["image_status"] = "image_analysis_returned"
    result["diagnostic"] = picture["diagnostic"]
    # A screenshot cannot prove a V3.2.61 problem still exists in V3.2.62.
    eligible = incident_preflight(job, suite_for(job["mode"]), repo, image_verified=True)
    if not eligible["ready_for_ai"]:
        result.update(status="diagnosed_no_patch", reason=";".join(eligible["preflight_reasons"]),
                      next="Obtain same-version reproduction before coding")
        return result

    evidence = {
        "image_status": picture["status"],
        "image_sha256": picture["image"]["sha256"],
        "diagnostic": picture["diagnostic"],
        "log_excerpt": log["excerpt"],
    }
    patch_before = state.job(key)["calls"]
    try:
        candidate = run_one(state, key, repo, True, acknowledge_cost,
                            max_daily, evidence=evidence,
                            retry_regression_once=retry_regression_once)
    except ValueError as exc:
        # The image analysis is already completed. Never silently retry a
        # rejected Codex patch or escalate without an explicit new command.
        result.update(status="needs_review", reason=type(exc).__name__,
                      next="Patch stage blocked; review source checkout and case status")
        return result

    result["patch_calls_reserved"] = state.job(key)["calls"] - patch_before
    result["candidate"] = candidate
    result["status"] = candidate["status"]
    result["code_modified"] = bool(candidate.get("changed_files"))
    result["candidate_verified"] = candidate["status"] == "candidate_passed"
    return result
