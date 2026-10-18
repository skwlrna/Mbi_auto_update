"""Stage24: paired, offline A/B *prompt-input* check of Stage23 review context.

Both arms use the SAME already-sanitized error context. Arm B appends only
the existing Stage23 bounded advisory inside the unchanged untrusted-log
fence. No Codex/model inference, external calls, game input, editing, saving
of prompts, screenshot acquisition, packet capture or upload occurs.

Model accuracy, remediation benefit and root cause identification CANNOT be
measured from this input-only test. Any heuristic-category drift is a warning.
"""
from __future__ import annotations

from collections import Counter
import itertools
import json
from pathlib import Path

import autopilot
import incident_coverage_audit as coverage
import incident_review_context as bridge
from error_memory import classify

MAX_PAIR_EVENTS = 12
INPUT_CONTEXT_LIMIT = 45000
DEFAULT_POLICY = {"allowed_change_prefixes": ["source/", "tests/", "changes/tests/"]}
FIXED_COUNTS = (
    "runtime_folders", "readable_reports", "skipped_reports",
    "eligible_runtime_reports", "sampled_reports", "sampling_cap_reached",
    "paired_with_advisory", "paired_without_advisory",
    "prompt_content_changed_as_expected", "prompt_contract_vetoes",
    "unexpected_prompt_difference", "heuristic_category_drift_warnings",
    "unreadable_or_ineligible", "unsafe_input_fence_vetoes",
)
# Strictly paired on ONE event/ONE sanitized context; never compare two
# independent error events or their unrelated heuristic labels.
FENCE_OPEN = "\n<untrusted-log>\n"
FENCE_CLOSE = "\n</untrusted-log>"


def _evaluate_one(folder: Path, root: Path) -> tuple[str, bool]:
    """Return fixed verdict + heuristic warning, never raw prompt or labels."""
    try:
        # Scope strictly to Stage21's bounded runtime events. The bridge
        # independently validates the direct folder and current error.txt.
        if (folder.is_symlink() or not folder.is_dir() or
                folder.parent.resolve(strict=True) != root.resolve(strict=True) or
                not coverage.ab.NAME.fullmatch(folder.name) or
                coverage._read(root, folder) is None):
            return "unreadable_or_ineligible", False
        context = autopilot.context_for(folder)
        if (not isinstance(context, str) or len(context) > INPUT_CONTEXT_LIMIT or
                FENCE_OPEN in context or FENCE_CLOSE in context or
                bridge.BEGIN in context or bridge.END in context):
            return "unsafe_input_fence_vetoes", False
        advisory = bridge.for_runtime_error(folder, root)
        if (not isinstance(advisory, str) or
                len(advisory) > bridge.MAX_ADVISORY or
                (advisory and not (
                    advisory.startswith(bridge.BEGIN + "\n") and
                    advisory.endswith("\n" + bridge.END)))):
            return "prompt_contract_vetoes", False
        baseline = autopilot.prompt_for(context, DEFAULT_POLICY)
        enriched = autopilot.prompt_for(context, DEFAULT_POLICY, advisory)
        # A real unchanged prompt is the correct outcome when the local
        # Stage22 data has no eligible quality-review hint.
        if baseline.count(FENCE_OPEN) != 1 or baseline.count(FENCE_CLOSE) != 1:
            return "prompt_contract_vetoes", False
        if not advisory:
            return ("paired_without_advisory", False) if baseline == enriched else (
                "unexpected_prompt_difference", False)
        expected = baseline.replace(FENCE_CLOSE, "\n" + advisory + FENCE_CLOSE, 1)
        if (enriched != expected or
                enriched.count(FENCE_OPEN) != 1 or
                enriched.count(FENCE_CLOSE) != 1 or
                "Never run git commit, git push, merge" not in enriched or
                "Do not claim a live-game test" not in enriched or
                len(enriched) > len(baseline) + bridge.MAX_ADVISORY + 1):
            return "unexpected_prompt_difference", False
        # Only a deterministic string heuristic is run in-memory here,
        # not Codex or any AI. A category shift cannot prove improvement.
        drift = (classify(baseline).get("suspected_category") !=
                 classify(enriched).get("suspected_category"))
        return "paired_with_advisory", bool(drift)
    except (OSError, UnicodeError, ValueError, TypeError, RuntimeError, KeyError):
        return "prompt_contract_vetoes", False


def _output(counts: Counter, status: str) -> dict[str, object]:
    n = counts["paired_with_advisory"] + counts["paired_without_advisory"]
    return {
        "schema": 1,
        "stage": 24,
        "status": status,
        "scope": "offline_paired_prompt_inputs_only",
        "sample": "local_offline_or_synthetic",
        "arm_a": "existing_sanitized_prompt",
        "arm_b": "same_prompt_plus_fixed_review_advisory",
        "counts": {key: counts[key] for key in FIXED_COUNTS},
        "paired_prompt_inputs": n,
        "exact_same_event_and_context": True,
        "review_advisory_not_incident_truth": True,
        "heuristic_drift_is_not_ai_accuracy": True,
        "model_responses_collected": 0,
        "codex_executions": 0,
        "game_runs": 0,
        "new_cli_queries": 0,
        "new_screenshots": 0,
        "network_requests": 0,
        "saved_prompt_files": 0,
        "human_ground_truth_verified": False,
        "diagnostic_accuracy_change": "not_measured",
        "remediation_success_change": "not_measured",
        "automatic_repair_authorized": False,
        "gameplay_authorized": False,
        "deployment_authorized": False,
    }


def evaluate(root: Path) -> dict[str, object]:
    """Bounded offline scan: exactly one input context shared by both arms.

    The sampling cap intentionally means that counts cannot estimate an
    error-rate or actual model improvement across all production events.
    """
    counts: Counter = Counter()
    try:
        if root.is_symlink() or not root.is_dir():
            return _output(counts, "root_unavailable")
        # Reject oversize roots *before* reading any event.
        folders = list(itertools.islice(root.iterdir(), coverage.ab.MAX_FOLDERS + 1))
        if len(folders) > coverage.ab.MAX_FOLDERS:
            return _output(counts, "folder_limit_exceeded")
        runtime = []
        for folder in folders:
            if (not coverage.ab.NAME.fullmatch(folder.name) or
                    folder.is_symlink() or not folder.is_dir()):
                continue
            counts["runtime_folders"] += 1
            if coverage._read(root, folder) is None:
                counts["skipped_reports"] += 1
                continue
            counts["readable_reports"] += 1
            runtime.append(folder)
        # Fixed lexical sampling does NOT affect watcher queue order.
        # It does not change user report files, mtime or processing priority.
        runtime.sort(key=lambda p: p.name)
        counts["eligible_runtime_reports"] = len(runtime)
        sampled = runtime[:MAX_PAIR_EVENTS]
        counts["sampled_reports"] = len(sampled)
        counts["sampling_cap_reached"] = int(len(runtime) > MAX_PAIR_EVENTS)
        for folder in sampled:
            verdict, drift = _evaluate_one(folder, root)
            counts[verdict] += 1
            if verdict == "paired_with_advisory":
                counts["prompt_content_changed_as_expected"] += 1
            if drift:
                counts["heuristic_category_drift_warnings"] += 1
        return _output(counts, "offline_complete")
    except (OSError, UnicodeError, ValueError, RuntimeError, TypeError):
        return _output(Counter(), "io_unavailable")


def main() -> int:
    root = Path.home() / "MabiAutoAI" / "runtime-reports"
    print(json.dumps(evaluate(root), ensure_ascii=False, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
