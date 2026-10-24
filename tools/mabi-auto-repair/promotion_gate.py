#!/usr/bin/env python3
"""Audit auto-repair Draft PR CI, and safely promote *only* with real-game signoff.

The 5-minute watcher may call audit() with no GitHub writes. A passed offline
regression does NOT prove game behavior. A merge requires an operator-written
attestation plus a separate explicit CLI action. Never create a release, tag,
or updater ZIP; versioned release workflows are distinct and cannot recycle
the existing V3.2.62 asset.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import subprocess
import sys
from pathlib import Path

REPO = "skwlrna/Mbi_auto_update"
PR_BRANCH = re.compile(r"auto-repair/([a-f0-9]{24})-([a-f0-9]{8})\Z")
SHA = re.compile(r"[a-f0-9]{40}\Z")
DIGEST = re.compile(r"[a-f0-9]{64}\Z")
TEST_FOLDERS = frozenset(("altering", "gathering", "crafting", "loot-retry", "retry-flow"))
RELEASE_VERSION = re.compile(r"V\d+\.\d+\.\d+\Z")


class GateError(ValueError):
    """A safety gate failed. Do not infer an approval from this error."""


def gh(args: list[str], timeout: int = 30) -> str:
    try:
        proc = subprocess.run(["gh", *args], text=True, capture_output=True,
                              timeout=timeout, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise GateError("GitHub CLI unavailable, blocked or timed out") from exc
    if proc.returncode:
        # Do not forward gh stdout or stderr: could contain credentials.
        raise GateError("GitHub CLI request failed: " + args[0])
    return proc.stdout.strip()


def api(path: str):
    if not path.startswith("repos/" + REPO + "/") or ".." in path:
        raise GateError("Unexpected GitHub API path")
    try:
        return json.loads(gh(["api", path]))
    except (ValueError, TypeError) as exc:
        raise GateError("Invalid GitHub API JSON") from exc


def _sha(s) -> bool:
    return isinstance(s, str) and SHA.fullmatch(s) is not None


def _candidate_files(files: object) -> list[str]:
    if not isinstance(files, list) or not 2 <= len(files) <= 12:
        raise GateError("Unexpected candidate file count")
    names = []
    source, test_folders = False, set()
    for file in files:
        if not isinstance(file, dict):
            raise GateError("Unexpected file metadata")
        name = file.get("filename")
        if not isinstance(name, str) or file.get("status") not in ("added", "modified"):
            raise GateError("Unexpected candidate path or file operation")
        if name in names or not name.endswith(".cs"):
            raise GateError("Duplicate or non-C# candidate file")
        if file.get("changes", 0) > 5000:
            raise GateError("Candidate diff exceeds safe review size")
        if name == "source/FishingAutomation/UpdateManager.cs":
            raise GateError("Repair PR cannot change version")
        if name.startswith("source/FishingAutomation/"):
            source = True
        elif name.startswith("tests/"):
            chunks = name.split("/")
            if len(chunks) < 3 or chunks[1] not in TEST_FOLDERS:
                raise GateError("Unregistered test path in candidate PR")
            test_folders.add(chunks[1])
        else:
            raise GateError("Non-C# production/test file in candidate PR")
        names.append(name)
    if not source or len(test_folders) != 1:
        raise GateError("One registered regression folder and C# source required")
    return sorted(names)


def audit(number: int) -> dict:
    """A read-only assessment; exact current head SHA and CI run required."""
    if not isinstance(number, int) or not 1 <= number <= 10_000_000:
        raise GateError("Invalid PR number")
    pr = api(f"repos/{REPO}/pulls/{number}")
    if not isinstance(pr, dict):
        raise GateError("Invalid PR record")
    url = f"https://github.com/{REPO}/pull/{number}"
    if pr.get("state") != "open" or pr.get("merged_at"):
        return {"pr": number, "url": url, "status": "pr_not_open",
                "may_merge": False}
    head, base = pr.get("head") or {}, pr.get("base") or {}
    head_sha, base_sha = head.get("sha"), base.get("sha")
    if not (_sha(head_sha) and _sha(base_sha)):
        raise GateError("PR head or base SHA invalid")
    match = PR_BRANCH.fullmatch(str(head.get("ref") or ""))
    if (not match or base.get("ref") != "main"
            or head.get("repo", {}).get("full_name") != REPO
            or base.get("repo", {}).get("full_name") != REPO):
        raise GateError("Not an approved first-party auto-repair PR")
    current_main = api(f"repos/{REPO}/branches/main")
    main_sha = current_main.get("commit", {}).get("sha")
    if not _sha(main_sha):
        raise GateError("Cannot verify current main SHA")
    if main_sha != base_sha or match.group(2) != main_sha[:8]:
        return {"pr": number, "url": url, "status": "stale_main",
                "head_sha": head_sha, "may_merge": False}
    body = pr.get("body") or ""
    if not (f"\x60{match.group(1)}\x60" in body and f"\x60{main_sha}\x60" in body):
        raise GateError("PR provenance metadata invalid")
    commits = api(f"repos/{REPO}/pulls/{number}/commits?per_page=100")
    if not isinstance(commits, list) or len(commits) != 1:
        raise GateError("Repair PR must contain exactly one candidate commit")
    only = commits[0]
    parents = only.get("parents") or []
    if (only.get("sha") != head_sha or len(parents) != 1
            or parents[0].get("sha") != main_sha):
        raise GateError("Repair PR commit chain diverges from main")
    files = api(f"repos/{REPO}/pulls/{number}/files?per_page=100")
    names = _candidate_files(files)
    if pr.get("changed_files") != len(names):
        raise GateError("PR files truncated or not identical")
    runs = api(f"repos/{REPO}/actions/runs?head_sha={head_sha}&per_page=100")
    if not isinstance(runs, dict) or not isinstance(runs.get("workflow_runs"), list):
        raise GateError("Cannot verify production CI")
    eligible = [
        r for r in runs["workflow_runs"]
        if (r.get("head_sha") == head_sha
            and r.get("name") == "Check V3 Production Engine"
            and r.get("path") == ".github/workflows/ci.yml"
            and r.get("event") == "pull_request"
            and r.get("head_repository", {}).get("full_name") == REPO)
    ]
    eligible.sort(key=lambda r: (r.get("created_at") or "", r.get("run_attempt") or 0,
                                 r.get("id") or 0), reverse=True)
    state = "ci_pending"
    run_id = None
    if eligible:
        latest = eligible[0]
        run_id = latest.get("id")
        if latest.get("status") == "completed":
            state = ("ci_passed_waiting_game" if latest.get("conclusion") == "success"
                     else "ci_failed")
    return {
        "pr": number, "url": url, "status": state,
        "head_sha": head_sha, "base_sha": main_sha,
        "is_draft": bool(pr.get("draft")), "ci_run_id": run_id,
        "changed_files": names, "may_merge": False,
        "reason": "Real-game and rollback signoff required before promotion",
    }


def check_attestation(report: Path, audited: dict) -> dict:
    """Require explicit human-authored per-head testing; never infer success."""
    if not report.is_file() or report.is_symlink() or report.stat().st_size > 8192:
        raise GateError("Missing or oversized real-game test report")
    try:
        data = json.loads(report.read_text(encoding="utf-8-sig"))
    except (OSError, UnicodeError, ValueError) as exc:
        raise GateError("Game-test report could not be parsed") from exc
    if not isinstance(data, dict):
        raise GateError("Game-test report must be a JSON object")
    if (data.get("schema") != 1 or data.get("pr") != audited["pr"]
            or data.get("tested_head_sha") != audited["head_sha"]
            or data.get("game_result") != "passed"
            or data.get("rollback_result") != "passed"):
        raise GateError("Real-game or rollback verification not completed on PR head")
    tester, description = data.get("tested_by"), data.get("test_details")
    if (not isinstance(tester, str) or not re.fullmatch(r"[a-zA-Z0-9_-]{3,80}", tester)
            or not isinstance(description, str) or not 20 <= len(description) <= 1000):
        raise GateError("Insufficient human test attribution and details")
    # Promotion must be tied to a distinct PR checkout/build and fresh
    # operator-collected candidate + stable rollback evidence. A quiet log
    # or CI run cannot automatically assert that the live game worked.
    evidence = data.get("game_evidence")
    if not isinstance(evidence, dict):
        raise GateError("Separate real-game session evidence is required")
    hashes = ("candidate_executable_sha256", "stable_executable_sha256",
              "candidate_proof_sha256", "rollback_proof_sha256")
    if (evidence.get("candidate_source_sha") != audited["head_sha"]
            or evidence.get("verification") != "operator_attested"
            or evidence.get("new_runtime_errors") != 0
            or not isinstance(evidence.get("session_id"), str)
            or not re.fullmatch(r"[a-f0-9]{24}", evidence["session_id"])
            or not all(isinstance(evidence.get(h), str) and DIGEST.fullmatch(evidence[h])
                       for h in hashes)
            or evidence["candidate_executable_sha256"] == evidence["stable_executable_sha256"]
            or evidence["candidate_proof_sha256"] == evidence["rollback_proof_sha256"]):
        raise GateError("Invalid candidate, game proof or rollback binding")
    try:
        tested = dt.datetime.fromisoformat(str(data.get("tested_at_utc", "")).replace("Z", "+00:00"))
    except ValueError as exc:
        raise GateError("Invalid real-game test timestamp") from exc
    now = dt.datetime.now(dt.timezone.utc)
    if tested.tzinfo is None or tested.utcoffset() != dt.timedelta(0):
        raise GateError("Game-test timestamp must be UTC")
    if not dt.timedelta(0) <= (now - tested) <= dt.timedelta(days=7):
        raise GateError("Game-test report too old or dated in the future")
    return {"pr": audited["pr"], "head_sha": audited["head_sha"],
            "game_test": "operator_attested", "rollback": "operator_attested",
            "note": "Operator supplied game and rollback evidence; live outcome is not AI-proven"}


def promote(number: int, report: Path, acknowledge_merge: bool) -> dict:
    if not acknowledge_merge:
        raise GateError("Explicit --acknowledge-merge required; no automatic promotion")
    details = audit(number)
    if details["status"] != "ci_passed_waiting_game":
        raise GateError("Latest production PR CI must pass on unchanged main and head")
    proof = check_attestation(report, details)
    # Draft conversion is an explicit human-signed-off action, never called by
    # scheduled watch. Merge checks GitHub branch restrictions; no admin bypass.
    if details["is_draft"]:
        gh(["pr", "ready", str(number), "--repo", REPO], 40)
    fresh = audit(number)
    if fresh["status"] != "ci_passed_waiting_game" or fresh["head_sha"] != details["head_sha"]:
        raise GateError("PR changed after game signoff; merge cancelled")
    gh(["pr", "merge", str(number), "--repo", REPO,
        "--squash", "--match-head-commit", details["head_sha"]], 120)
    return {"pr": number, "status": "merge_requested",
            "tested_head_sha": proof["head_sha"],
            "release": "blocked_pending_new_version_and_release_validation",
            "next": "Build and test a new updater-compatible version with exact-main CI, "
                    "Windows executable smoke, asset checksums, rollback and explicit release signoff"}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Offline-safe auto-repair PR CI and merge gate")
    sub = parser.add_subparsers(dest="action", required=True)
    check = sub.add_parser("audit", help="Read-only status of a candidate PR")
    check.add_argument("pr", type=int)
    merge = sub.add_parser("promote", help="Human-attested merge, never a release")
    merge.add_argument("pr", type=int)
    merge.add_argument("--game-test-report", type=Path, required=True)
    merge.add_argument("--acknowledge-merge", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.action == "audit":
            result = audit(args.pr)
        else:
            result = promote(args.pr, args.game_test_report, args.acknowledge_merge)
        print(json.dumps(result, indent=2, ensure_ascii=True))
        return 0
    except (GateError, OSError) as exc:
        # Do not output credentials, raw gh stderr or game screenshots.
        print(json.dumps({"error": type(exc).__name__, "detail": str(exc)[:200]}),
              file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
