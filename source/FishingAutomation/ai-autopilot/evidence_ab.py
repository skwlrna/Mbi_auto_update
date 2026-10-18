"""Offline, privacy-minimized A/B inventory for Stage 14 passive CLI evidence.

Compares the *same* error.txt before and after its validated Stage-14
annotation using the existing deterministic error_memory.classify heuristic.
It DOES NOT call an LLM/Codex, measure causal diagnostic accuracy, modify
an error, perform game actions, or upload any content.

Run locally without arguments for an aggregate-only summary of
~/MabiAutoAI/runtime-reports. JSON emitted to stdout is counts/categories
only; paths, identifiers, text, screenshots, raw payloads are never emitted.
"""
from __future__ import annotations

from collections import Counter
from dataclasses import dataclass
import itertools
import json
from pathlib import Path
import re
from typing import Optional

from error_memory import classify

BEGIN = ("BEGIN READ-ONLY CLI DIAGNOSTIC (observed before error; "
         "not decoded packet data or permission to act)")
END = ("END READ-ONLY CLI DIAGNOSTIC; never infer receipt, "
       "completion, or safe automation from this evidence")

COMMANDS = (
    "status", "get_activity", "get_altering_works",
    "get_items", "get_alterable_items",
)
GENERIC = frozenset({
    "cli_response_accepted_not_game_event",
    "cli_parser_schema_unverified",
    "cli_timeout_state_unknown", "cli_unavailable_state_unknown",
    "cli_reports_game_off", "cli_reports_option_off",
    "cli_explicit_disconnect", "cli_invalid_json",
    "cli_unexpected_schema", "cli_rejected", "cli_failed_unknown",
})
ALLOWED = {
    "status": GENERIC | {"cli_connected"},
    "get_activity": GENERIC | {"cli_activity_observed",
                               "cli_gathering_in_progress"},
    "get_altering_works": GENERIC | {
        "cli_altering_work_running",
        "cli_work_rows_completed_receipt_unknown",
        "cli_no_work_rows_completion_unknown",
        "cli_mixed_or_pending_works",
        "cli_altering_possible_removed_not_completed",
        "cli_altering_possible_finished_receipt_unknown",
    },
    "get_items": GENERIC | {"cli_inventory_shape_only_no_delta"},
    "get_alterable_items": GENERIC | {"cli_recipe_catalog_shape_only"},
}
LINE = re.compile(r"^(status|get_activity|get_altering_works|get_items|"
                  r"get_alterable_items)=([a-z_]+);age_ms=(0|[1-9][0-9]{0,3})$")
NAME = re.compile(r"^runtime_[0-9]{8}_[0-9]{6}_[0-9]{3}_[0-9a-f]{8}$")
MAX_INPUT = 16_384
MAX_FOLDERS = 200

TRIAGE_BEGIN = "BEGIN COMMON INCIDENT TRIAGE V1"
TRIAGE_END = "END COMMON INCIDENT TRIAGE V1"
TRIAGE_AREAS = {"unknown", "gathering", "altering", "crafting",
                "fishing", "dungeon", "abyss", "scheduler"}
TRIAGE_COVERAGE = {"unknown_mode_no_verified_state", "domain_cli_hint_only",
    "domain_data_missing", "no_verified_crafting_state",
    "no_verified_fishing_state", "no_verified_dungeon_state",
    "no_verified_scheduler_run_at_error"}
TRIAGE_HINTS = {"no_domain_specific_evidence", "gathering_reported_in_progress",
    "possible_work_removal_not_completion",
    "work_completed_rows_receipt_unknown", "work_reported_in_progress",
    "empty_work_list_not_completion"}
TRIAGE_CONNECTIVITY = {"connectivity_unverified", "read_only_cli_connected_only",
    "cli_disconnect_reported", "cli_timeout_not_disconnect",
    "cli_unavailable_not_disconnect", "cli_game_off_reported"}


def _remove_stage18_triage(lines: list[str]) -> Optional[str]:
    """Remove only one exact, versioned and allowlisted Stage18 block.

    An arbitrary incident-like block must not contaminate the Stage15
    paired pre-diagnostic arm or silently pass as verified evidence.
    """
    stripped = [line.rstrip("\r\n") for line in lines]
    first = [i for i, line in enumerate(stripped)
             if line.startswith("BEGIN COMMON INCIDENT TRIAGE")]
    last = [i for i, line in enumerate(stripped)
            if line.startswith("END COMMON INCIDENT TRIAGE")]
    if not first and not last:
        return "".join(lines)
    if len(first) != 1 or len(last) != 1:
        return None
    a, b = first[0], last[0]
    if b != a + 7 or stripped[a] != TRIAGE_BEGIN or stripped[b] != TRIAGE_END:
        return None
    def val(index: int, key: str, allowed: set[str]) -> bool:
        return stripped[index] in {key + "=" + item for item in allowed}
    if not (val(a + 1, "area", TRIAGE_AREAS) and
            val(a + 2, "coverage", TRIAGE_COVERAGE) and
            val(a + 3, "hint", TRIAGE_HINTS) and
            val(a + 4, "connectivity", TRIAGE_CONNECTIVITY) and
            stripped[a + 5] in {"relevant_facts=" + str(i) for i in range(6)} and
            stripped[a + 6] ==
            "verified_cause=false;gameplay_authorized=false;repair_authorized=false"):
        return None
    return "".join(lines[:a] + lines[b + 1:])


SCREEN_BEGIN = "BEGIN VERIFIED SCREEN INCIDENT V1"
SCREEN_END = "END VERIFIED SCREEN INCIDENT V1"
SCREEN_SIGNALS = {
    ("gathering", "gathering_stop_control_double_checked"),
    ("abyss", "abyss_clear_pair_guard_passed"),
    ("abyss", "abyss_result_guard_passed"),
}
SCREEN_COMPARISONS = {
    "no_comparable_cli_state", "compatible_gathering_hints_only",
}


def _remove_stage19_screen(lines: list[str]) -> Optional[str]:
    """Exclude only an exact Stage19 diagnostic block from Stage15 baseline.

    An arbitrary or duplicated block is invalid, not independent evidence.
    """
    stripped = [line.rstrip("\r\n") for line in lines]
    first = [i for i, row in enumerate(stripped)
             if row.startswith("BEGIN VERIFIED SCREEN INCIDENT")]
    last = [i for i, row in enumerate(stripped)
            if row.startswith("END VERIFIED SCREEN INCIDENT")]
    if not first and not last:
        return "".join(lines)
    if len(first) != 1 or len(last) != 1:
        return None
    a, b = first[0], last[0]
    if b != a + 6 or stripped[a] != SCREEN_BEGIN or stripped[b] != SCREEN_END:
        return None
    area = stripped[a + 1].removeprefix("area=")
    signal = stripped[a + 2].removeprefix("signal=")
    comparison = stripped[a + 3].removeprefix("comparison=")
    age = re.fullmatch(r"age_ms=(0|[1-9][0-9]{0,3}|5000)",
                       stripped[a + 4])
    if ((area, signal) not in SCREEN_SIGNALS or
            stripped[a + 1] != "area=" + area or
            stripped[a + 2] != "signal=" + signal or
            stripped[a + 3] != "comparison=" + comparison or
            comparison not in SCREEN_COMPARISONS or
            (area != "gathering" and
             comparison == "compatible_gathering_hints_only") or
            age is None or int(age.group(1)) > 5000 or
            stripped[a + 5] !=
            "verified_cause=false;gameplay_authorized=false;repair_authorized=false"):
        return None
    return "".join(lines[:a] + lines[b + 1:])


@dataclass(frozen=True)
class Evidence:
    status: str  # absent, invalid, valid
    baseline: str
    enriched: str
    rows: tuple[tuple[str, str], ...] = ()


def parse(text: str) -> Evidence:
    """Accept only one exact bounded, well-ordered Stage-14 block.

    A formatted block is not authenticated evidence: users can manually edit
    error.txt. The result is solely for diagnostic coverage evaluation.
    """
    lines = text.splitlines(keepends=True)
    start = [i for i, s in enumerate(lines)
             if s.rstrip("\r\n").startswith("BEGIN READ-ONLY CLI DIAGNOSTIC")]
    finish = [i for i, s in enumerate(lines)
              if s.rstrip("\r\n").startswith("END READ-ONLY CLI DIAGNOSTIC")]
    if not start and not finish:
        return Evidence("absent", text, text)
    if len(start) != 1 or len(finish) != 1:
        return Evidence("invalid", text, text)
    a, b = start[0], finish[0]
    if (b <= a or b - a < 2 or b - a > len(COMMANDS) + 1 or
        lines[a].rstrip("\r\n") != BEGIN or
        lines[b].rstrip("\r\n") != END):
        return Evidence("invalid", text, text)

    entries = []
    last_index = -1
    for row in lines[a + 1:b]:
        match = LINE.fullmatch(row.rstrip("\r\n"))
        if not match:
            return Evidence("invalid", text, text)
        command, outcome, ms_text = match.groups()
        index = COMMANDS.index(command)
        if index <= last_index or outcome not in ALLOWED[command]:
            return Evidence("invalid", text, text)
        if int(ms_text) > 5000:
            return Evidence("invalid", text, text)
        last_index = index
        entries.append((command, outcome))

    # Remove only the exact annotation. Preserve the same original log and
    # surrounding context for a paired comparison, not two different errors.
    baseline = _remove_stage18_triage(lines[:a] + lines[b + 1:])
    if baseline is None:
        return Evidence("invalid", text, text)
    baseline = _remove_stage19_screen(baseline.splitlines(keepends=True))
    if baseline is None:
        return Evidence("invalid", text, text)
    return Evidence("valid", baseline, text, tuple(entries))


def signals(rows: tuple[tuple[str, str], ...]) -> tuple[str, ...]:
    observed = set()
    for command, outcome in rows:
        if outcome in {
            "cli_timeout_state_unknown", "cli_unavailable_state_unknown",
            "cli_explicit_disconnect", "cli_reports_game_off",
            "cli_reports_option_off",
        }:
            observed.add("connection_or_query_failure_hint")
        if outcome in {"cli_parser_schema_unverified", "cli_invalid_json",
                       "cli_unexpected_schema"}:
            observed.add("schema_validation_hint")
        if outcome == "cli_gathering_in_progress":
            observed.add("gathering_progress_hint")
        if outcome == "cli_altering_work_running":
            observed.add("altering_work_in_progress_hint")
        if outcome == "cli_work_rows_completed_receipt_unknown":
            observed.add("receipt_not_verified_hint")
        if outcome == "cli_no_work_rows_completion_unknown":
            observed.add("empty_work_list_not_completion_hint")
        if outcome == "cli_altering_possible_removed_not_completed":
            observed.add("work_removal_possible_not_completion_hint")
        if outcome == "cli_altering_possible_finished_receipt_unknown":
            observed.add("work_finished_possible_receipt_unknown_hint")
        if outcome == "cli_inventory_shape_only_no_delta":
            observed.add("inventory_delta_unknown_hint")
    return tuple(sorted(observed))


def compare_text(error_text: str) -> dict[str, object]:
    """Category shifts are *warnings*, never proof of improved accuracy."""
    evidence = parse(error_text)
    before = classify(evidence.baseline)["suspected_category"]
    after = classify(evidence.enriched)["suspected_category"]
    return {
        "evidence_format": evidence.status,
        "queries_with_validated_shape": len(evidence.rows),
        "additional_hints": signals(evidence.rows),
        "baseline_heuristic_category": before,
        "enriched_heuristic_category": after,
        "heuristic_category_changed": evidence.status == "valid" and before != after,
        "ground_truth_verified": False,
        "ai_analysis_run": False,
        "gameplay_authorized": False,
        "diagnostic_accuracy_proven": False,
    }


def summarize(root: Path) -> dict[str, object]:
    """Read bounded local error.txt files only. No network, edits or AI calls.

    A symlink, non-runtime directory or oversized/binary report is rejected.
    The result never contains an event ID, filename, user text or exception.
    """
    totals = Counter()
    hints = Counter()
    before = Counter()
    after = Counter()
    try:
        if root.is_symlink() or not root.is_dir():
            return _finish(totals, hints, before, after, "root_unavailable")
        # +1 catches large workspaces without loading an unbounded list.
        dirs = list(itertools.islice(root.iterdir(), MAX_FOLDERS + 1))
        if len(dirs) > MAX_FOLDERS:
            return _finish(totals, hints, before, after, "folder_limit_exceeded")
        for folder in dirs:
            if not NAME.fullmatch(folder.name) or folder.is_symlink() or not folder.is_dir():
                continue
            totals["runtime_folders"] += 1
            report = folder / "error.txt"
            try:
                if (report.is_symlink() or not report.is_file() or
                    report.stat().st_size > MAX_INPUT or
                    report.stat().st_size == 0):
                    totals["skipped_reports"] += 1
                    continue
                raw = report.read_bytes()
                if len(raw) > MAX_INPUT:
                    totals["skipped_reports"] += 1
                    continue
                text = raw.decode("utf-8-sig", errors="strict")
                if "\x00" in text:
                    totals["skipped_reports"] += 1
                    continue
            except (OSError, UnicodeError, ValueError):
                totals["skipped_reports"] += 1
                continue
            report_summary = compare_text(text)
            totals["readable_reports"] += 1
            kind = str(report_summary["evidence_format"])
            totals[kind + "_reports"] += 1
            if kind == "valid":
                totals["validated_query_shapes"] += int(
                    report_summary["queries_with_validated_shape"])
                if report_summary["heuristic_category_changed"]:
                    totals["potential_heuristic_label_bias"] += 1
                for hint in report_summary["additional_hints"]:
                    hints[str(hint)] += 1
                before[str(report_summary["baseline_heuristic_category"])] += 1
                after[str(report_summary["enriched_heuristic_category"])] += 1
        return _finish(totals, hints, before, after, "offline_complete")
    except OSError:
        return _finish(totals, hints, before, after, "io_unavailable")


def _finish(totals: Counter, hints: Counter, before: Counter,
            after: Counter, status: str) -> dict[str, object]:
    keys = ("runtime_folders", "readable_reports", "skipped_reports",
            "absent_reports", "invalid_reports", "valid_reports",
            "validated_query_shapes", "potential_heuristic_label_bias")
    # Fixed schema only, no path strings, user text, logs, IDs or raw errors.
    return {
        "schema": 1,
        "status": status,
        "sample": "local_offline_or_synthetic",
        "counts": {key: totals[key] for key in keys},
        "hint_counts": dict(sorted(hints.items())),
        "baseline_heuristic_categories": dict(sorted(before.items())),
        "enriched_heuristic_categories": dict(sorted(after.items())),
        "interpretation": "evidence_availability_and_label_drift_only",
        "codex_runs": 0,
        "game_runs": 0,
        "ground_truth_verified": False,
        "causal_improvement_measured": False,
        "automatic_recovery_enabled": False,
    }


def main() -> int:
    root = Path.home() / "MabiAutoAI" / "runtime-reports"
    print(json.dumps(summarize(root), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
