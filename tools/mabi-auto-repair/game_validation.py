#!/usr/bin/env python3
"""Evidence-bound, human-supervised *real game* verification for a Draft repair PR.

Never launches the game, injects input, replaces the installed macro, declares
success from a quiet log, merges, or publishes. Requires a separate candidate
checkout/executable, unchanged stable executable, and two fresh evidence files.
The human must run/observe the game and its rollback themselves.
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import re
import secrets
import subprocess
import sys
from pathlib import Path

import promotion_gate
import repair

SHA = re.compile(r"[a-f0-9]{40}\Z")
ERROR_SUMMARY = re.compile(r"runtime-errors/\d{4}-\d{2}-\d{2}/[^/]+/summary\.json\Z")
MAX_SUMMARY = 8192
MAX_PROOF = 25 * 1024 * 1024
MAX_EXECUTABLE = 1024 * 1024 * 1024


class EvidenceError(ValueError):
    pass


def digest_file(path: Path, limit: int = MAX_PROOF) -> str:
    if not path.is_file() or path.is_symlink() or path.stat().st_size == 0:
        raise EvidenceError("Evidence/executable missing, symlinked or empty")
    if path.stat().st_size > limit:
        raise EvidenceError("Evidence file exceeds safe size")
    value = hashlib.sha256()
    with path.open("rb") as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b""):
            value.update(block)
    return value.hexdigest()


def utc_now() -> dt.datetime:
    return dt.datetime.now(dt.timezone.utc)


def repo_head(root: Path) -> str:
    if not (root / "source/FishingAutomation/FishingAutomation.csproj").is_file():
        raise EvidenceError("Expected standalone candidate source tree")
    try:
        head = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"],
                              capture_output=True, text=True, timeout=20, check=False)
        dirty = subprocess.run(["git", "-C", str(root), "status", "--porcelain"],
                               capture_output=True, text=True, timeout=20, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise EvidenceError("Cannot inspect candidate Git checkout") from exc
    sha = head.stdout.strip()
    if head.returncode or dirty.returncode or dirty.stdout.strip() or not SHA.fullmatch(sha):
        raise EvidenceError("Candidate source checkout is dirty or has unknown SHA")
    return sha


def sync_log_root(log_root: Path) -> None:
    """Fail closed if the GitHub error uploader's latest entries are unavailable."""
    if not (log_root / ".git").exists():
        raise EvidenceError("Mbi_auto_log must be a Git checkout for fresh evidence")
    try:
        result = subprocess.run(["git", "-C", str(log_root), "pull", "--ff-only", "origin", "main"],
                                capture_output=True, text=True, timeout=90, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise EvidenceError("Cannot synchronize GitHub runtime error log") from exc
    if result.returncode:
        raise EvidenceError("Runtime error Git sync failed; verification blocked")


def incident_snapshot(log_root: Path) -> dict:
    """Read only local metadata, never edit or upload the error log checkout."""
    if not log_root.is_dir() or log_root.is_symlink():
        raise EvidenceError("Error log checkout not found")
    result = {}
    for file in log_root.glob("runtime-errors/*/*/summary.json"):
        relative = file.relative_to(log_root).as_posix()
        if ERROR_SUMMARY.fullmatch(relative) and file.is_file():
            if file.is_symlink():
                raise EvidenceError("Symlinked runtime error summary")
            stat = file.stat()
            result[relative] = [stat.st_size, stat.st_mtime_ns]
    return result


def _within(child: Path, directory: Path) -> bool:
    return child.resolve().is_relative_to(directory.resolve())


def _load_session(session_file: Path) -> dict:
    if not session_file.is_file() or session_file.is_symlink() or session_file.stat().st_size > 1024 * 1024:
        raise EvidenceError("Missing or oversized game verification session")
    try:
        session = json.loads(session_file.read_text(encoding="utf-8"))
    except (OSError, ValueError, UnicodeError) as exc:
        raise EvidenceError("Invalid game verification session") from exc
    if session.get("schema") != 1 or session.get("status") != "awaiting_game_test":
        raise EvidenceError("Game verification session not active")
    return session


def start(pr: int, candidate_root: Path, candidate_exe: Path, stable_exe: Path,
          log_root: Path, output: Path) -> dict:
    audit = promotion_gate.audit(pr)
    if audit["status"] != "ci_passed_waiting_game":
        raise EvidenceError("Exact candidate PR CI must pass before real-game observation")
    head = repo_head(candidate_root)
    if head != audit["head_sha"]:
        raise EvidenceError("Candidate local checkout does not match audited PR HEAD")
    if not _within(candidate_exe, candidate_root):
        raise EvidenceError("Candidate executable must be inside isolated PR checkout")
    if _within(stable_exe, candidate_root) or candidate_exe.resolve() == stable_exe.resolve():
        raise EvidenceError("Stable installed macro must be outside isolated candidate checkout")
    candidate_hash, stable_hash = (digest_file(candidate_exe, MAX_EXECUTABLE),
                                   digest_file(stable_exe, MAX_EXECUTABLE))
    if candidate_hash == stable_hash:
        raise EvidenceError("Candidate and installed stable executable are identical")
    if output.exists() or output.is_symlink():
        raise EvidenceError("Existing session cannot be replaced")
    if _within(output, candidate_root) or _within(output, log_root):
        raise EvidenceError("Session must be outside candidate and log checkout")
    sync_log_root(log_root)
    initial = incident_snapshot(log_root)
    now = utc_now()
    session = {
        "schema": 1, "status": "awaiting_game_test", "id": secrets.token_hex(12),
        "pr": pr, "tested_head_sha": head, "ci_run_id": audit["ci_run_id"],
        "started_at_utc": now.isoformat(), "candidate_root": str(candidate_root.resolve()),
        "candidate_exe": str(candidate_exe.resolve()),
        "stable_exe": str(stable_exe.resolve()), "log_root": str(log_root.resolve()),
        "candidate_sha256": candidate_hash, "stable_sha256": stable_hash,
        "known_runtime_errors": initial,
        "game_result": "not_verified", "rollback_result": "not_verified",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8") as handle:
        json.dump(session, handle, indent=2, ensure_ascii=True)
    return {"status": "awaiting_game_test", "session": str(output),
            "pr": pr, "head_sha": head, "known_errors": len(initial),
            "next": "Run isolated candidate in real game, capture fresh proof, "
                    "restore stable app and capture rollback proof. No auto-pass."}


def inspect(session_file: Path) -> dict:
    session = _load_session(session_file)
    if repo_head(Path(session["candidate_root"])) != session["tested_head_sha"]:
        raise EvidenceError("Candidate source SHA changed since session start")
    if digest_file(Path(session["candidate_exe"]), MAX_EXECUTABLE) != session["candidate_sha256"]:
        raise EvidenceError("Candidate executable changed since session start")
    if digest_file(Path(session["stable_exe"]), MAX_EXECUTABLE) != session["stable_sha256"]:
        raise EvidenceError("Stable installed executable changed; rollback unsafe")
    known = session["known_runtime_errors"]
    sync_log_root(Path(session["log_root"]))
    current = incident_snapshot(Path(session["log_root"]))
    new = sorted(name for name, stat in current.items() if name not in known or stat != known[name])
    return {
        "pr": session["pr"], "head_sha": session["tested_head_sha"],
        "status": "blocked_new_runtime_error" if new else "awaiting_human_game_evidence",
        "new_runtime_errors": len(new),
        "error_summaries": new[:15],
        "candidate_sha256": session["candidate_sha256"],
        "stable_sha256": session["stable_sha256"],
        "game_verified": False,
        "note": "No new runtime error does not prove the live game fix worked",
    }


def _fresh_proof(path: Path, session: dict) -> dict:
    if not path.is_file() or path.is_symlink() or path.suffix.lower() not in (".png", ".jpg", ".jpeg", ".log", ".txt"):
        raise EvidenceError("Fresh PNG/JPEG/log game evidence required")
    if _within(path, Path(session["candidate_root"])) or _within(path, Path(session["log_root"])):
        raise EvidenceError("Proof must be stored outside candidate source and error checkout")
    start_time = dt.datetime.fromisoformat(session["started_at_utc"]).timestamp()
    if path.stat().st_mtime < start_time:
        raise EvidenceError("Evidence predates this game test session")
    return {"sha256": digest_file(path), "bytes": path.stat().st_size,
            "kind": path.suffix.lower()[1:]}


def attest(session_file: Path, candidate_proof: Path, rollback_proof: Path,
           tester: str, details: str, candidate_passed: bool,
           rollback_passed: bool, acknowledged: bool, output: Path) -> dict:
    if not acknowledged:
        raise EvidenceError("Explicit real-game observation acknowledgement required")
    if not candidate_passed or not rollback_passed:
        raise EvidenceError("Both candidate and rollback must be witnessed as successful")
    if not re.fullmatch(r"[a-zA-Z0-9_-]{3,80}", tester) or not 40 <= len(details) <= 1000:
        raise EvidenceError("Tester and meaningful real-game reproduction details required")
    session = _load_session(session_file)
    observed = inspect(session_file)
    if observed["new_runtime_errors"]:
        raise EvidenceError("New runtime errors detected: game candidate cannot pass")
    if candidate_proof.resolve() == rollback_proof.resolve():
        raise EvidenceError("Candidate and rollback need separate proof files")
    candidate = _fresh_proof(candidate_proof, session)
    stable = _fresh_proof(rollback_proof, session)
    if candidate["sha256"] == stable["sha256"]:
        raise EvidenceError("Candidate and rollback evidence must be distinct")
    if output.exists() or output.is_symlink():
        raise EvidenceError("Existing game report cannot be overwritten")
    if _within(output, Path(session["candidate_root"])) or _within(output, Path(session["log_root"])):
        raise EvidenceError("Report must not modify candidate or error checkout")
    current_audit = promotion_gate.audit(session["pr"])
    if (current_audit.get("status") != "ci_passed_waiting_game"
            or current_audit.get("head_sha") != session["tested_head_sha"]):
        raise EvidenceError("PR SHA or latest CI changed since game test start")
    when = utc_now()
    report = {
        "schema": 1, "pr": session["pr"], "tested_head_sha": session["tested_head_sha"],
        "game_result": "passed", "rollback_result": "passed",
        "tested_by": tester, "tested_at_utc": when.isoformat(),
        "test_details": details,
        "game_evidence": {
            "session_id": session["id"], "candidate_source_sha": session["tested_head_sha"],
            "candidate_executable_sha256": session["candidate_sha256"],
            "stable_executable_sha256": session["stable_sha256"],
            "candidate_proof_sha256": candidate["sha256"],
            "rollback_proof_sha256": stable["sha256"],
            "new_runtime_errors": 0, "verification": "operator_attested",
        },
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8") as handle:
        json.dump(report, handle, indent=2, ensure_ascii=True)
    return {"status": "operator_attestation_recorded", "report": str(output),
            "pr": session["pr"], "head_sha": session["tested_head_sha"],
            "game_verified_automatically": False,
            "next": "Manual promotion gate still requires --acknowledge-merge"}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Passive real-game evidence gate; does not launch game")
    sub = parser.add_subparsers(dest="action", required=True)
    initial = sub.add_parser("start")
    initial.add_argument("--pr", type=int, required=True)
    initial.add_argument("--candidate-root", type=Path, required=True)
    initial.add_argument("--candidate-exe", type=Path, required=True)
    initial.add_argument("--stable-exe", type=Path, required=True)
    initial.add_argument("--log-root", type=Path, required=True)
    initial.add_argument("--out", type=Path, required=True)
    checking = sub.add_parser("inspect")
    checking.add_argument("--session", type=Path, required=True)
    complete = sub.add_parser("attest")
    complete.add_argument("--session", type=Path, required=True)
    complete.add_argument("--candidate-proof", type=Path, required=True)
    complete.add_argument("--rollback-proof", type=Path, required=True)
    complete.add_argument("--tested-by", required=True)
    complete.add_argument("--test-details", required=True)
    complete.add_argument("--candidate-passed", action="store_true")
    complete.add_argument("--rollback-passed", action="store_true")
    complete.add_argument("--acknowledge-real-game", action="store_true")
    complete.add_argument("--out", type=Path, required=True)
    opts = parser.parse_args(argv)
    try:
        if opts.action == "start":
            result = start(opts.pr, opts.candidate_root, opts.candidate_exe,
                           opts.stable_exe, opts.log_root, opts.out)
        elif opts.action == "inspect":
            result = inspect(opts.session)
        else:
            result = attest(opts.session, opts.candidate_proof, opts.rollback_proof,
                            opts.tested_by, opts.test_details, opts.candidate_passed,
                            opts.rollback_passed, opts.acknowledge_real_game, opts.out)
        print(json.dumps(result, ensure_ascii=True, indent=2))
        return 0
    except (EvidenceError, promotion_gate.GateError, OSError, ValueError) as exc:
        print(json.dumps({"error": type(exc).__name__,
                          "detail": repair.sanitized(str(exc))[:180]}), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
