"""Stage 22: deterministic human-review priorities from Stage21 aggregate counts.

NOT a severity/incident/root-cause model, an autonomous AI agent, an
approval gate, or a gameplay instruction. No LLM, network, new CLI, screenshots,
file writes or game inputs. Prints only fixed-vocabulary, non-identifying
aggregate review queues. Reads the same bounded local log root as Stage21.
"""
from __future__ import annotations

import json
from pathlib import Path
from typing import Any

import incident_coverage_audit as audit

MAX_REVIEW_ITEMS = 12
# Order expresses *which diagnostic data-quality gap to inspect first*,
# not how serious the user's gameplay failure was. Never assign a
# confidence percentage or infer actual false positives/negatives.
POLICY = (
    ("P1", "invalid_annotations", "review_fixed_annotation_format",
     ("reports_with_invalid_annotations",)),
    ("P1", "mode_label_mismatch", "review_mode_mapping",
     ("screen_mode_mismatch", "triage_mode_mismatch")),
    ("P2", "self_inconsistent_annotations", "review_companion_evidence_labels",
     ("annotation_inconsistencies",)),
    ("P2", "missing_both_annotations", "review_opt_in_and_evidence_ttl",
     ("neither_annotation",)),
    ("P2", "unreadable_local_report", "review_local_report_readability",
     ("skipped_reports",)),
    ("P3", "screen_without_cli", "review_existing_cli_coverage",
     ("screen_only",)),
    ("P3", "cli_without_screen", "review_existing_screen_guard_coverage",
     ("cli_only",)),
    ("P3", "noncomparable_dual_hints", "review_validated_semantic_mapping",
     ("unsupported_semantic_comparison",)),
    ("P3", "unknown_report_mode", "review_known_mode_header",
     ("unknown_report_mode",)),
    ("P4", "cli_query_failure_hint", "review_existing_query_failure_labels",
     ("cli_query_failure_hints",)),
)
VALID_STATUSES = {
    "offline_complete", "root_unavailable", "folder_limit_exceeded",
    "io_unavailable",
}


def _allowed(report: object) -> bool:
    """Refuse arbitrary external/malformed dictionaries (no partial queue)."""
    if not isinstance(report, dict):
        return False
    exact_fields = {
        "schema", "stage", "status", "source", "counts", "by_mode",
        "verified_gameplay_conflicts", "diagnostic_accuracy_change",
        "absence_interpretation", "comparison_interpretation",
        "ground_truth_verified", "gameplay_authorized", "repair_authorized",
        "new_cli_queries", "new_screen_captures", "ai_runs", "game_runs",
    }
    if set(report) != exact_fields:
        return False
    if (type(report.get("schema")) is not int or report["schema"] != 1 or
            type(report.get("stage")) is not int or report["stage"] != 21 or
            type(report.get("status")) is not str or
            report["status"] not in VALID_STATUSES or
            report.get("source") != "local_offline_or_synthetic_reports" or
            report.get("ground_truth_verified") is not False or
            report.get("gameplay_authorized") is not False or
            report.get("repair_authorized") is not False or
            report.get("verified_gameplay_conflicts") != "not_measurable" or
            report.get("diagnostic_accuracy_change") != "not_measured" or
            report.get("absence_interpretation") !=
            "missing_annotation_not_missing_game_event" or
            report.get("comparison_interpretation") !=
            "only_gathering_activity_hint_supported_not_causal_confirmation" or
            any(type(report.get(k)) is not int or report[k] != 0 for k in
                ("new_cli_queries", "new_screen_captures", "ai_runs", "game_runs"))):
        return False
    counts = report.get("counts")
    by_mode = report.get("by_mode")
    if not isinstance(counts, dict) or not isinstance(by_mode, dict):
        return False
    # Exact Stage21 count dictionary, bounded by the maximum files examined.
    if set(counts) != set(audit.COUNTS):
        return False
    if any(type(value) is not int or not 0 <= value <= audit.ab.MAX_FOLDERS
           for value in counts.values()):
        return False
    if (counts["runtime_folders"] > audit.ab.MAX_FOLDERS or
            counts["readable_reports"] + counts["skipped_reports"] !=
            counts["runtime_folders"]):
        return False
    total = counts["readable_reports"]
    invalid = counts["reports_with_invalid_annotations"]
    if (invalid > total or
            sum(counts[k] for k in (
                "both_annotations", "cli_only", "screen_only",
                "neither_annotation")) != total - invalid or
            counts["with_cli_annotation"] != (
                counts["both_annotations"] + counts["cli_only"]) or
            counts["with_screen_annotation"] != (
                counts["both_annotations"] + counts["screen_only"]) or
            counts["with_triage_annotation"] > total - invalid or
            counts["supported_gathering_comparison"] >
            counts["both_annotations"] or
            counts["annotation_inconsistencies"] >
            counts["both_annotations"] or
            counts["unsupported_semantic_comparison"] >
            counts["both_annotations"] + counts["screen_only"] or
            counts["cli_query_failure_hints"] >
            counts["with_cli_annotation"] or
            counts["unknown_report_mode"] > total):
        return False
    if (counts["supported_gathering_comparison"] +
        counts["annotation_inconsistencies"] +
        counts["unsupported_semantic_comparison"] >
        counts["both_annotations"] + counts["screen_only"]):
        return False
    if any(counts[k] > invalid for k in (
            "invalid_cli_annotation", "invalid_screen_annotation",
            "invalid_triage_annotation")):
        return False
    if any(counts[k] > total - invalid for k in (
            "screen_mode_mismatch", "triage_mode_mismatch")):
        return False
    if (not set(by_mode).issubset(audit.MODES) or
            len(by_mode) > len(audit.MODES)):
        return False
    if any(
        not isinstance(row, dict) or set(row) != set(audit.FIELDS) or
        any(type(value) is not int or not 0 <= value <= total
            for value in row.values()) or
        row["readable"] == 0 or
        row["valid_cli"] > row["readable"] or
        row["valid_screen"] > row["readable"] or
        row["both"] > min(row["valid_cli"], row["valid_screen"]) or
        row["supported_comparison"] > row["both"] or
        row["annotation_inconsistency"] > row["both"]
        for row in by_mode.values()
    ):
        return False
    if sum(row["readable"] for row in by_mode.values()) != total:
        return False
    for field, expected in (
        ("valid_cli", counts["with_cli_annotation"]),
        ("valid_screen", counts["with_screen_annotation"]),
        ("both", counts["both_annotations"]),
        ("supported_comparison", counts["supported_gathering_comparison"]),
        ("annotation_inconsistency", counts["annotation_inconsistencies"]),
    ):
        if sum(row[field] for row in by_mode.values()) != expected:
            return False
    # A non-complete Stage21 scan must never be promoted into a
    # diagnostic worklist. It may have zero or partial counts.
    return True


def prioritize(report: object) -> dict[str, Any]:
    """Return a human-review *data-quality* queue with no actual AI work."""
    valid = _allowed(report)
    status = report["status"] if valid else "invalid_stage21_summary"
    count = report["counts"]["readable_reports"] if valid else 0
    ready = bool(valid and status == "offline_complete" and count > 0)
    items: list[dict[str, Any]] = []
    if ready:
        counts = report["counts"]
        for tier, category, next_check, signals in POLICY:
            matched = sum(counts[key] for key in signals)
            if not matched:
                continue
            items.append({
                "tier": tier,
                "category": category,
                "occurrences": matched,
                # Fixed action describes READ-ONLY human diagnosis, never
                # approval to execute CLI, perform game input or repair.
                "next_review": next_check,
                "meaning": "diagnostic_annotation_quality_only",
            })
            if len(items) >= MAX_REVIEW_ITEMS:
                break
    return {
        "schema": 1,
        "stage": 22,
        "source_stage": 21,
        "source_status": status,
        "status": ("review_candidates_available" if items else
                   "no_priority_gaps_observed" if ready else
                   "insufficient_or_untrusted_aggregate"),
        "scope": "diagnostic_review_not_incident_severity",
        "sample": "local_offline_or_synthetic",
        "readable_reports": count,
        "review_items": items,
        "priorities_are_deterministic_policy_not_ground_truth": True,
        "actual_error_severity": "unknown",
        "false_positive_rate": "not_measurable",
        "false_negative_rate": "not_measurable",
        "verified_root_causes": "not_measurable",
        "ai_analysis_run": False,
        "gameplay_authorized": False,
        "repair_authorized": False,
        "deployment_authorized": False,
        "new_cli_queries": 0,
        "new_screen_captures": 0,
        "network_requests": 0,
        "game_runs": 0,
    }


def main() -> int:
    source_root = Path.home() / "MabiAutoAI" / "runtime-reports"
    summary = audit.summarize(source_root)
    print(json.dumps(prioritize(summary), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
