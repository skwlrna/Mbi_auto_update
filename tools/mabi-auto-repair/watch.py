#!/usr/bin/env python3
"""On-PC one-shot watcher for newly published Mabi_auto_log runtime errors.

Designed for Windows Task Scheduler every five minutes, not a long-running
hidden network agent. Never auto-deploys or interacts with the game. One
atomic, persistent claim per incident and one per fingerprint prevent retries
even when the scheduled process restarts or crashes.

Start with: watch.py init --log-root <log checkout>
Then schedule: watch.py poll --log-root <log checkout> --repo <clean app checkout>
Paid recurring analysis requires BOTH --execute and --acknowledge-cost.
"""
from __future__ import annotations

import argparse
import json
import shutil
import sqlite3
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path

import repair
import pipeline
import publish_candidate

TASK_SQL = """
CREATE TABLE IF NOT EXISTS watch_meta(
 name TEXT PRIMARY KEY,
 value TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS watch_events(
 source TEXT PRIMARY KEY,
 fingerprint TEXT NOT NULL DEFAULT '',
 status TEXT NOT NULL,
 reason TEXT NOT NULL DEFAULT '',
 recorded_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS watch_prs(
 source TEXT PRIMARY KEY,
 pr_url TEXT NOT NULL,
 recorded_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS watch_fingerprints(
 fingerprint TEXT PRIMARY KEY,
 source TEXT NOT NULL,
 recorded_at TEXT NOT NULL
);
"""


def utc() -> str:
    return datetime.now(timezone.utc).isoformat(timespec="seconds")


def prepare(state: repair.State) -> None:
    state.conn.executescript(TASK_SQL)


def scan(log_root: Path) -> list[tuple[str, dict]]:
    # Include every existing log when taking the initial baseline, even if
    # the user had 100+ historical incidents. Process oldest first so the
    # state record for a deduplicated fingerprint points to the newest event.
    return list(reversed(repair.fetch_local(log_root, 1_000_000)))


def pull_logs(root: Path) -> None:
    if not root.is_dir() or not (root / ".git").exists():
        raise ValueError("Mbi_auto_log must be a local Git checkout")
    try:
        process = subprocess.run(["git", "-C", str(root), "pull", "--ff-only"],
                                 capture_output=True, text=True, timeout=90, check=False)
    except (OSError, subprocess.TimeoutExpired) as exc:
        raise ValueError("Cannot update local Mbi_auto_log checkout") from exc
    if process.returncode:
        # Do not leak full Git remote details or credentials in the scheduled
        # task history; the operator can run git pull manually for details.
        raise ValueError("Git log pull failed; check GitHub authentication and conflicts")


def initialize(state: repair.State, root: Path) -> dict:
    prepare(state)
    if state.conn.execute("SELECT 1 FROM watch_meta WHERE name='initialized'").fetchone():
        return {"status": "already_initialized", "baseline_count": state.conn.execute(
            "SELECT COUNT(*) FROM watch_events").fetchone()[0],
            "note": "Existing event baseline retained; new incidents were not discarded"}
    entries = scan(root)
    now = utc()
    state.conn.execute("BEGIN IMMEDIATE")
    try:
        state.conn.executemany(
            "INSERT OR IGNORE INTO watch_events(source,fingerprint,status,recorded_at) "
            "VALUES (?, '', 'baseline', ?)",
            [(origin, now) for origin, _ in entries],
        )
        state.conn.execute(
            "INSERT INTO watch_meta(name,value) VALUES ('initialized',?)", (now,))
        state.conn.execute("COMMIT")
    except BaseException:
        state.conn.execute("ROLLBACK")
        raise
    return {"status": "initialized", "baseline_count": len(entries),
            "codex_calls": 0, "note": "Only future incidents will be acted upon"}


def claim_event(state: repair.State, source: str, fingerprint: str,
                reserve_fingerprint: bool) -> str:
    """Atomically claim one source; optional fingerprint claim for paid calls.

    Return claimed, duplicate_source or duplicate_fingerprint. Claims are
    committed *before* any network/AI call so crashes cannot double bill.
    """
    now = utc()
    state.conn.execute("BEGIN IMMEDIATE")
    try:
        if state.conn.execute("SELECT 1 FROM watch_events WHERE source=?", (source,)).fetchone():
            state.conn.execute("COMMIT")
            return "duplicate_source"
        if reserve_fingerprint and state.conn.execute(
                "SELECT 1 FROM watch_fingerprints WHERE fingerprint=?", (fingerprint,)).fetchone():
            state.conn.execute(
                "INSERT INTO watch_events(source,fingerprint,status,recorded_at) "
                "VALUES (?,?,'duplicate_fingerprint',?)", (source, fingerprint, now))
            state.conn.execute("COMMIT")
            return "duplicate_fingerprint"
        state.conn.execute(
            "INSERT INTO watch_events(source,fingerprint,status,recorded_at) "
            "VALUES (?,?,'claimed',?)", (source, fingerprint, now))
        if reserve_fingerprint:
            state.conn.execute(
                "INSERT INTO watch_fingerprints(fingerprint,source,recorded_at) "
                "VALUES (?,?,?)", (fingerprint, source, now))
        state.conn.execute("COMMIT")
        return "claimed"
    except BaseException:
        state.conn.execute("ROLLBACK")
        raise


def finalize(state: repair.State, source: str, status: str, reason: str = "") -> None:
    state.conn.execute(
        "UPDATE watch_events SET status=?, reason=?, recorded_at=? WHERE source=?",
        (status, repair.sanitized(reason)[:200], utc(), source))


def poll(state: repair.State, root: Path, repo: Path, execute: bool,
         cost_ack: bool, auto_pr: bool = False) -> dict:
    if execute and not cost_ack:
        raise ValueError("--acknowledge-cost required for recurring Codex analysis")
    prepare(state)
    if auto_pr:
        if not execute or not cost_ack:
            raise ValueError("Automatic PR requires --execute and --acknowledge-cost")
        if not shutil.which("gh"):
            raise ValueError("GitHub CLI gh required before automatic PR analysis")
        publish_candidate.command(["gh", "auth", "status"], repo)
    if not state.conn.execute("SELECT 1 FROM watch_meta WHERE name='initialized'").fetchone():
        raise ValueError("Watcher has no baseline; run 'watch.py init' first")
    version = repair.current_source_version(repo)
    # Scan the local log snapshot after git pull. Empty/new/invalid reports
    # are ignored or recorded without AI calls. No daily quota by default.
    events = scan(root)
    counts = {}
    recent = []

    def emit(status: str, source: str, detail: str = "", pr_url: str = ""):
        counts[status] = counts.get(status, 0) + 1
        record = {"source": source, "status": status,
                  "reason": repair.sanitized(detail)[:160]}
        if pr_url:
            record["pr_url"] = pr_url
        recent.append(record)

    for source, summary in events:
        if state.conn.execute("SELECT 1 FROM watch_events WHERE source=?", (source,)).fetchone():
            continue
        if not isinstance(summary, dict) or summary.get("schema") != 1 or summary.get("source") != "runtime":
            if claim_event(state, source, "", False) == "claimed":
                finalize(state, source, "invalid_report")
                emit("invalid_report", source)
            continue
        event_version = repair.sanitized(summary.get("version"))
        if event_version != version:
            if claim_event(state, source, "", False) == "claimed":
                finalize(state, source, "skipped_version", "source_version_not_reproduced")
                emit("skipped_version", source)
            continue
        fingerprint = repair.digest(summary)
        claimed = claim_event(state, source, fingerprint, reserve_fingerprint=execute)
        if claimed == "duplicate_source":
            continue
        if claimed == "duplicate_fingerprint":
            emit("duplicate_fingerprint", source)
            continue
        try:
            key = state.add(source, summary)
            if not key:
                finalize(state, source, "invalid_report")
                emit("invalid_report", source)
                continue
            if not execute:
                # Non-paid watchers still record a notification, not a fix.
                finalize(state, source, "observed_unpaid")
                emit("observed_unpaid", source)
                continue
            if state.job(key)["calls"] or state.job(key)["status"] != "new":
                finalize(state, source, "duplicate_fingerprint")
                emit("duplicate_fingerprint", source)
                continue
            if not all(shutil.which(cmd) for cmd in ("git", "dotnet", "codex")):
                finalize(state, source, "missing_runtime", "Git, dotnet and Codex CLI required")
                emit("missing_runtime", source)
                continue
            try:
                repair.clean_git_repo(repo)
            except ValueError:
                finalize(state, source, "checkout_not_clean")
                emit("checkout_not_clean", source)
                continue
            # Real image/log diagnosis, then isolated source/test patch if and
            # only if the same-version safety gate is satisfied.
            result = pipeline.investigate(state, key, repo, root, True, True)
            status = str(result["status"])
            reason = ""
            pr_url = ""
            if status == "candidate_passed" and auto_pr:
                try:
                    pr_url = publish_candidate.publish(state, key, repo, result)
                except publish_candidate.PublishBlocked as exc:
                    status = "pr_needs_review"
                    reason = str(exc)
                else:
                    status = "pr_opened"
                    state.conn.execute(
                        "INSERT INTO watch_prs(source,pr_url,recorded_at) VALUES (?,?,?)",
                        (source, pr_url, utc()))
            finalize(state, source, status, reason)
            emit(status, source, reason, pr_url)
        except (ValueError, OSError, sqlite3.Error, TimeoutError) as exc:
            # One-shot failure; human can inspect and trigger a *new* manual
            # command. Scheduled retries would risk duplicate paid model calls.
            finalize(state, source, "needs_review", type(exc).__name__)
            emit("needs_review", source, type(exc).__name__)
    return {"status": "poll_completed", "source_version": version,
            "scanned": len(events), "new_count": len(recent),
            "outcomes": counts, "new_events": recent[-30:],
            "automatic_pr": auto_pr, "automatic_deployment": False}


def report_status(state: repair.State) -> dict:
    prepare(state)
    items = [dict(row) for row in state.conn.execute(
        "SELECT source,fingerprint,status,reason,recorded_at "
        "FROM watch_events ORDER BY recorded_at DESC,source DESC LIMIT 20")]
    counts = {r["status"]: r["n"] for r in state.conn.execute(
        "SELECT status,COUNT(*) AS n FROM watch_events GROUP BY status")}
    for record in items:
        link = state.conn.execute(
            "SELECT pr_url FROM watch_prs WHERE source=?", (record["source"],)).fetchone()
        if link:
            record["pr_url"] = link["pr_url"]
    return {"status": "watch_status", "counts": counts, "recent_events": items}


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(
        description="Local new-error watcher; no AI unless opted in; no auto-deploy")
    parser.add_argument("--state", type=Path, default=Path.home() / ".MabiAutoRepair")
    sub = parser.add_subparsers(dest="mode", required=True)
    setup = sub.add_parser("init", help="Mark existing incidents as baseline, no AI")
    setup.add_argument("--log-root", required=True, type=Path)
    setup.add_argument("--no-pull", action="store_true")
    checking = sub.add_parser("poll", help="Check new errors exactly once")
    checking.add_argument("--log-root", required=True, type=Path)
    checking.add_argument("--repo", required=True, type=Path)
    checking.add_argument("--no-pull", action="store_true")
    checking.add_argument("--execute", action="store_true")
    checking.add_argument("--acknowledge-cost", action="store_true")
    checking.add_argument("--auto-pr", action="store_true",
                          help="Opt in to creating DRAFT GitHub PRs for tested candidates")
    sub.add_parser("status", help="Show event history and outcomes")
    args = parser.parse_args(argv)
    state = None
    try:
        state = repair.State(args.state)
        if args.mode in ("init", "poll") and not args.no_pull:
            pull_logs(args.log_root)
        if args.mode == "init":
            result = initialize(state, args.log_root)
        elif args.mode == "poll":
            result = poll(state, args.log_root, args.repo, args.execute,
                          args.acknowledge_cost, args.auto_pr)
        else:
            result = report_status(state)
        print(json.dumps(result, ensure_ascii=True, indent=2))
        return 0
    except (ValueError, OSError, sqlite3.Error, json.JSONDecodeError) as exc:
        print(json.dumps({"error": repair.sanitized(str(exc))},
                         ensure_ascii=True), file=sys.stderr)
        return 2
    finally:
        if state is not None:
            state.conn.close()


if __name__ == "__main__":
    raise SystemExit(main())
