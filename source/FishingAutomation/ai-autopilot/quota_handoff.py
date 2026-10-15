"""Bounded local ChatGPT handoff when Codex usage is exhausted.

No ChatGPT API call, remote upload, game input, account introspection,
background process, or automatic entitlement reset. Only opaque hashes and
heuristic enum labels may be exported.
"""
import datetime as dt
from pathlib import Path
import re
import secrets
from typing import Any

QUOTA_MARKERS = (
    "usage limit reached", "you have hit your usage limit",
    "you've hit your usage limit", "your usage limit has been reached",
    "codex usage limit", "plan usage limit", "weekly usage limit",
    "insufficient_quota", "quota exceeded", "quota_exceeded",
    "out of credits", "not enough credits", "credit balance is exhausted",
    "billing hard limit", "사용량 한도에 도달", "사용량을 모두 사용",
)
REASON = "codex_quota_exhausted"
MAX_ITEMS = 20
VALID_ID = re.compile(r"^[0-9a-f]{16}$")
SAFE_LABELS = frozenset((
    "receipt_state", "cli_state", "visual_recognition",
    "scheduler_transition", "timing_stall", "input_control", "unclassified"
))


def exhausted(*details: str) -> bool:
    """429/rate limit alone does NOT prove subscription quota exhaustion."""
    return any(marker in detail.lower() for detail in details if isinstance(detail, str)
               for marker in QUOTA_MARKERS)


def pause_record() -> dict[str, str]:
    return {"id": secrets.token_hex(16),
            "since": dt.datetime.now(dt.timezone.utc).isoformat(),
            "reason": REASON}


def resume_authorized(request: dict[str, Any], pause: dict[str, Any]) -> bool:
    return (isinstance(request, dict) and isinstance(pause, dict) and
            request.get("action") == "retry_after_user_confirmed_reset" and
            isinstance(pause.get("id"), str) and len(pause["id"]) == 32 and
            secrets.compare_digest(str(request.get("pause_id", "")), pause["id"]))


def handoff_manifest(pending: list[Path], reason: str, identify, classify,
                     prior: list[dict[str, str]] | None = None) -> dict[str, Any]:
    """Does not copy evidence, paths, screenshots, CLI output or raw error text."""
    entries = []
    safe_prior = prior if isinstance(prior, list) else []
    for item in safe_prior[:MAX_ITEMS]:
        if not isinstance(item, dict):
            continue
        eid, label = item.get("event_id", ""), item.get("suspected_category", "")
        if VALID_ID.fullmatch(eid):
            entries.append({"event_id": eid,
                            "suspected_category": label if label in SAFE_LABELS else "unclassified"})
    total_ids = {e["event_id"] for e in entries}
    for directory in pending:
        if len(entries) >= MAX_ITEMS:
            break
        eid = identify(directory)
        if not VALID_ID.fullmatch(eid) or eid in total_ids:
            continue
        try:
            label = classify(directory)
        except (OSError, ValueError, UnicodeError):
            label = "unclassified"
        if label not in SAFE_LABELS:
            label = "unclassified"
        entries.append({"event_id": eid, "suspected_category": label})
        total_ids.add(eid)
    return {
        "schema_version": 1,
        "status": reason if reason in {REASON, "daily_limit"} else "manual_review",
        "queued_total": len(pending) + len(safe_prior),
        "items": entries,
        "includes_raw_logs": False,
        "automatic_chatgpt_upload": False,
        "automatic_game_replay": False,
        "user_must_review_before_sharing": True,
    }
