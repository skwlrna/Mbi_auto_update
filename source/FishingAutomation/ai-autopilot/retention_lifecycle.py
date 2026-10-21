"""Offline, locally recoverable retirement of finished AI Git worktrees.

Never touches the user's original error folders, Codex result folders, or
Git history. The watcher singleton lock must be held by the caller; an
explicit --retention-reset may also archive interrupted legacy attempts.
"""
from __future__ import annotations

import hashlib
import json
from pathlib import Path
import re
import subprocess
import zipfile

from worktree_retention import (_existing_report, _git, _registrations,
                                _same_worktree_path, IDENTIFIER)

FINISHED = frozenset({
    "blocked", "needs_review", "tests_failed", "tests_passed_review_required",
    "interrupted_review_required",
})
MAX_FILES = 35000
MAX_BYTES = 512 * 1024 * 1024


def _linked(path: Path) -> bool:
    """Reject Windows junctions/reparse-backed directories as well as symlinks."""
    return path.is_symlink() or bool(getattr(path, "is_junction", lambda: False)())


def _sha256(stream) -> str:
    digest = hashlib.sha256()
    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
        digest.update(chunk)
    return digest.hexdigest()


def _source_dirs(config: dict, event_id: str, result: dict) -> list[tuple[str, Path]]:
    """Only owned worktree, matching result folder and bounded evidence folder."""
    root = Path(config["worktrees_dir"]).expanduser()
    sources = [("worktree", root / event_id)]
    result_root = Path(config["results_dir"]).expanduser()
    result_dir = result_root / event_id
    if _linked(result_root) or _linked(result_dir):
        raise ValueError("result_reparse_path_blocked")
    if result_dir.is_dir():
        sources.append(("results", result_dir))
    # In-app runtime errors are stored separately from external MCP reports.
    # Copy both known, bounded evidence sources without deleting either one.
    event = result.get("event", "")
    if isinstance(event, str) and re.fullmatch(r"[A-Za-z0-9_.-]{1,128}", event):
        for config_key, label in (("reports_dir", "evidence"),
                                  ("runtime_reports_dir", "runtime-evidence")):
            if config_key not in config:
                continue
            reports_root = Path(config[config_key]).expanduser()
            if _linked(reports_root):
                raise ValueError("evidence_reparse_path_blocked")
            evidence = reports_root / event
            if _linked(evidence):
                raise ValueError("evidence_reparse_path_blocked")
            if evidence.is_dir():
                sources.append((label, evidence))
    return sources


def _files(sources: list[tuple[str, Path]]) -> list[tuple[str, Path, int]]:
    entries = []
    size = 0
    for prefix, root in sources:
        if _linked(root) or not root.is_dir():
            raise ValueError("recovery_root_unavailable")
        for file in root.rglob("*"):
            if _linked(file):
                raise ValueError("recovery_symlink_detected")
            if not file.is_file():
                continue
            relative = file.relative_to(root).as_posix()
            name = prefix + "/" + relative
            if any(part in (".", "..") for part in relative.split("/")):
                raise ValueError("recovery_path_invalid")
            length = file.stat().st_size
            size += length
            if len(entries) >= MAX_FILES or size > MAX_BYTES:
                raise ValueError("recovery_archive_limit")
            entries.append((name, file, length))
    return sorted(entries)


def _snapshot(entries: list[tuple[str, Path, int]]) -> dict[str, str]:
    values = {}
    for name, path, length in entries:
        if _linked(path) or not path.is_file() or path.stat().st_size != length:
            raise ValueError("recovery_input_changed")
        with path.open("rb") as source:
            values[name] = _sha256(source)
    return values


def _verify_archive(path: Path, event_id: str, expected: dict[str, str]) -> bool:
    try:
        if _linked(path) or not path.is_file():
            return False
        with zipfile.ZipFile(path) as archive:
            manifest = json.loads(archive.read("_recovery/manifest.json"))
            if (manifest.get("schema") != 1 or
                    manifest.get("event_id") != event_id or
                    manifest.get("files") != expected or
                    set(archive.namelist()) != set(expected) | {"_recovery/manifest.json"}):
                return False
            entries = archive.infolist()
            if (len(entries) > MAX_FILES + 1 or
                    sum(info.file_size for info in entries) > MAX_BYTES + 1024 * 1024):
                return False
            for name, digest in expected.items():
                with archive.open(name) as source:
                    if _sha256(source) != digest:
                        return False
            return True
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, RuntimeError):
        return False


def archive_finished_one(config: dict, event_id: str, *,
                         allow_interrupted: bool = False) -> dict:
    """Checksum-copy first, compare again, then call Git worktree remove.

    When allow_interrupted=True the caller MUST hold the watcher lock after
    stopping the watcher. This is for explicit user-authorized legacy reset.
    """
    if not IDENTIFIER.fullmatch(event_id):
        return {"status": "blocked", "reason": "invalid_event_id"}
    root = Path(config["worktrees_dir"]).expanduser()
    if _linked(root):
        return {"status": "blocked", "reason": "worktree_root_symlink"}
    folder = root / event_id
    if _linked(folder) or not folder.is_dir():
        return {"status": "blocked", "reason": "worktree_not_a_directory"}
    registered = _registrations(config)
    owner = registered.get(folder.resolve())
    if owner is None:
        return {"status": "blocked", "reason": "untrusted_or_unregistered_owner"}
    result = _existing_report(config, event_id)
    state = result.get("status")
    if not allow_interrupted and state not in FINISHED:
        return {"status": "blocked", "reason": "not_terminal"}
    if result and (result.get("event_id") != event_id or
                   (result.get("worktree") is not None and
                    not _same_worktree_path(result.get("worktree"), folder))):
        return {"status": "blocked", "reason": "result_identity_mismatch"}
    archive_root = Path(config.get(
        "worktree_archives_dir",
        str(Path.home() / "MabiAutoAI" / "worktree-archives"))).expanduser()
    if _linked(archive_root):
        return {"status": "blocked", "reason": "archive_root_symlink"}
    destination = archive_root / (event_id + ".recovery.zip")
    temporary = archive_root / (event_id + ".recovery.zip.partial")
    try:
        archive_root.mkdir(parents=True, exist_ok=True)
        sources = _source_dirs(config, event_id, result)
        entries = _files(sources)
        # Never silently omit a present result or diagnostic folder. A
        # missing/malformed record remains identifiable in the archive notes.
        if (Path(config["results_dir"]).expanduser() / event_id).is_dir() and (
                "results/result.json" not in {item[0] for item in entries}
                and result):
            return {"status": "blocked", "reason": "result_record_missing_from_backup"}
        if not any(x[0].startswith("worktree/") for x in entries):
            return {"status": "blocked", "reason": "empty_worktree"}
        before = _snapshot(entries)
        if temporary.exists() or _linked(temporary):
            return {"status": "blocked", "reason": "partial_backup_requires_review"}
        if not destination.exists():
            manifest = {"schema": 1, "event_id": event_id,
                        "original_result_status": state or "missing",
                        "files": before,
                        "recovery": "Extract worktree/ into an isolated folder. Review changes against the recorded Git base; do not restore stale .git worktree pointers."}
            with zipfile.ZipFile(temporary, "x", compression=zipfile.ZIP_DEFLATED,
                                 allowZip64=True) as archive:
                for name, file, _ in entries:
                    if _linked(file):
                        raise ValueError("recovery_symlink_detected")
                    archive.write(file, name)
                archive.writestr("_recovery/manifest.json",
                                 json.dumps(manifest, ensure_ascii=False, indent=2))
            if not _verify_archive(temporary, event_id, before):
                return {"status": "blocked", "reason": "archive_checksum_failed"}
            temporary.replace(destination)
        if not _verify_archive(destination, event_id, before):
            return {"status": "blocked", "reason": "existing_archive_conflicts"}
        if _snapshot(_files(_source_dirs(config, event_id, _existing_report(config, event_id)))) != before:
            return {"status": "blocked", "reason": "source_changed_after_backup"}
        # Reconfirm registration, path and result gate immediately before removal.
        if _registrations(config).get(folder.resolve()) != owner:
            return {"status": "blocked", "reason": "owner_changed_before_remove"}
        latest = _existing_report(config, event_id)
        if not allow_interrupted and latest.get("status") not in FINISHED:
            return {"status": "blocked", "reason": "result_changed_before_remove"}
        removed = _git(owner, "worktree", "remove", "--force", str(folder))
        if removed.returncode:
            return {"status": "blocked", "reason": "git_remove_failed_backup_kept"}
        return {"status": "archived", "reason": "verified_recovery_archive_then_git_remove",
                "event_id": event_id, "archive": str(destination)}
    except (OSError, ValueError, subprocess.TimeoutExpired, zipfile.BadZipFile) as exc:
        return {"status": "blocked", "reason": "backup_" + type(exc).__name__}


def reset_preview(config: dict) -> dict:
    """Read-only classification even when legacy results are missing."""
    root = Path(config["worktrees_dir"]).expanduser()
    report = {"count": 0, "recoverable": [], "blocked": [], "states": {}}
    if _linked(root):
        report["error"] = "worktree_root_symlink"
        return report
    if not root.is_dir():
        return report
    registered = _registrations(config)
    for folder in sorted(root.iterdir()):
        if not folder.is_dir():
            continue
        report["count"] += 1
        result = _existing_report(config, folder.name)
        state = result.get("status", "missing_result")
        report["states"][state] = report["states"].get(state, 0) + 1
        entry = {"id": folder.name, "state": state}
        if (not IDENTIFIER.fullmatch(folder.name) or _linked(folder)
                or folder.resolve() not in registered):
            entry["reason"] = "untrusted_or_unregistered"
            report["blocked"].append(entry)
        else:
            report["recoverable"].append(entry)
    return report


def reset_legacy_batch(config: dict) -> dict:
    """Explicit offline reset, caller enforces SingletonLock and user intent."""
    inventory = reset_preview(config)
    outputs = []
    for candidate in inventory["recoverable"][:100]:
        outcome = archive_finished_one(config, candidate["id"],
                                       allow_interrupted=True)
        outputs.append({"id": candidate["id"], "status": outcome["status"],
                        "reason": outcome["reason"]})
    return {"initial_count": inventory["count"],
            "archive_and_reclaimed": sum(x["status"] == "archived" for x in outputs),
            "blocked": inventory["blocked"] +
                       [x for x in outputs if x["status"] != "archived"],
            "results": outputs, "evidence_deleted": False,
            "original_results_deleted": False}
