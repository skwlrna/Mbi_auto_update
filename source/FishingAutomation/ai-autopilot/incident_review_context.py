"""Stage23: bounded, passive Stage22 review advisory for EXISTING Codex prompt.

Does not schedule, prioritize or launch AI work; no file writes, packets,
screenshots, CLI commands, UI/game input, deployment or network. The global
aggregate review priorities are NOT evidence about this specific error.
"""
from __future__ import annotations

from pathlib import Path

import incident_coverage_audit as coverage
import incident_priority_review as review

TOP_ITEMS = 3
MAX_ADVISORY = 750
BEGIN = "BEGIN AGGREGATE DIAGNOSTIC REVIEW V1"
END = "END AGGREGATE DIAGNOSTIC REVIEW V1"
# Nothing from file paths, filenames, user text or a data-provided policy.
CATEGORIES = frozenset(item[1] for item in review.POLICY)
NEXT_CHECKS = frozenset(item[2] for item in review.POLICY)
TIERS = frozenset(("P1", "P2", "P3", "P4"))


def for_runtime_error(folder: Path, runtime_root: Path) -> str:
    """Fail closed for anything except the existing, direct runtime error path.

    Advisory covers the *whole local summary*, not the event being repaired.
    A malformed/incomplete/oversized root leaves Codex's normal prompt
    entirely unchanged.
    """
    try:
        if (runtime_root.is_symlink() or not runtime_root.is_dir() or
                folder.is_symlink() or not folder.is_dir() or
                not coverage.ab.NAME.fullmatch(folder.name) or
                folder.parent.resolve(strict=True) !=
                runtime_root.resolve(strict=True) or
                coverage._read(runtime_root, folder) is None):
            return ""
        summary = coverage.summarize(runtime_root)
        outcome = review.prioritize(summary)
        if (outcome.get("status") != "review_candidates_available" or
                outcome.get("scope") != "diagnostic_review_not_incident_severity" or
                outcome.get("repair_authorized") is not False or
                outcome.get("gameplay_authorized") is not False or
                outcome.get("ai_analysis_run") is not False):
            return ""
        items = outcome.get("review_items")
        if not isinstance(items, list) or not items:
            return ""
        parts = [
            BEGIN,
            "scope=global_aggregate_not_this_incident",
            "meaning=evidence_quality_review_not_gameplay_severity",
            "no_root_cause_or_ai_accuracy_verified=true",
        ]
        for entry in items[:TOP_ITEMS]:
            if (not isinstance(entry, dict) or
                    entry.get("tier") not in TIERS or
                    entry.get("category") not in CATEGORIES or
                    entry.get("next_review") not in NEXT_CHECKS or
                    type(entry.get("occurrences")) is not int or
                    not 1 <= entry["occurrences"] <= coverage.ab.MAX_FOLDERS):
                return ""
            # Double-check the complete, *fixed* tier/category/check tuple.
            if not any(
                (entry["tier"], entry["category"], entry["next_review"]) ==
                (tier, category, check)
                for tier, category, check, _ in review.POLICY
            ):
                return ""
            parts.append(
                "review=" + entry["tier"] + ":" +
                entry["category"] + ":" + entry["next_review"] + ":" +
                str(entry["occurrences"])
            )
        parts += [
            "gameplay_authorized=false;repair_authorized=false;deployment_authorized=false",
            END,
        ]
        advisory = "\n".join(parts)
        return advisory if len(advisory) <= MAX_ADVISORY else ""
    except (OSError, UnicodeError, ValueError, TypeError, RuntimeError):
        return ""
