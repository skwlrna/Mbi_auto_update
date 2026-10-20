"""Bounded, read-only check for an already-created AI draft PR.

Do not run Codex twice for byte-identical error evidence while a previous
review candidate is waiting. Unlike similar-incident matching, this requires
an EXACT evidence hash and an actual recorded draft PR. Old or failed changes
never block new diagnosis. No GitHub network queries or game actions.
"""
from __future__ import annotations

import json
from pathlib import Path
import re
from typing import Any

_SIGNATURE = re.compile(r"[0-9a-f]{64}\Z")
_EVENT = re.compile(r"[0-9a-f]{16}\Z")
_PR_URL = re.compile(
    r"https://github\.com/skwlrna/Mbi_auto_update/pull/[1-9][0-9]*/?\Z"
)


def existing_draft_for_evidence(
    signature: str | None, processed: list[dict[str, Any]],
    results_dir: Path, current_mtime: float, window_seconds: int = 86400
) -> dict[str, str] | None:
    if (not isinstance(signature, str) or not _SIGNATURE.fullmatch(signature)
            or not isinstance(processed, list) or
            not isinstance(current_mtime, (float, int)) or
            window_seconds < 1 or window_seconds > 86400):
        return None
    # Bound disk work. This is a single local watcher under SingletonLock.
    for entry in reversed(processed[-500:]):
        if not isinstance(entry, dict):
            continue
        event = entry.get("id")
        if (entry.get("signature") != signature or
                entry.get("status") != "tests_passed_review_required" or
                not isinstance(event, str) or not _EVENT.fullmatch(event)):
            continue
        past_time = entry.get("evidence_mtime")
        if type(past_time) not in (float, int):
            continue
        elapsed = current_mtime - past_time
        if elapsed < 0 or elapsed > window_seconds:
            continue
        report = results_dir / event / "result.json"
        try:
            if (report.is_symlink() or not report.is_file() or
                    report.stat().st_size > 131072):
                continue
            result = json.loads(report.read_text(encoding="utf-8-sig"))
            if not isinstance(result, dict):
                continue
            pr = result.get("pr")
            if (result.get("event_id") != event or
                    result.get("status") != "tests_passed_review_required" or
                    not isinstance(pr, dict) or
                    pr.get("status") != "draft_created"):
                continue
            url = pr.get("url")
            if not isinstance(url, str) or not _PR_URL.fullmatch(url):
                continue
            return {"event_id": event, "url": url}
        except (OSError, ValueError, TypeError, UnicodeError):
            continue
    return None
