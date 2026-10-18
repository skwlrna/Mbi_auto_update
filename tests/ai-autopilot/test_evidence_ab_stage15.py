"""Stage 15: deterministic offline A/B coverage, tampering and privacy checks.

Only synthetic local error files. No game, CLI, network, Codex,
worktree editing or production macro modifications.
"""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import evidence_ab as ab

spec = importlib.util.spec_from_file_location("stage15_autopilot", AI / "autopilot.py")
assert spec and spec.loader
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)

BLOCK = (
    ab.BEGIN + "\n"
    "status=cli_connected;age_ms=10\n"
    "get_altering_works=cli_work_rows_completed_receipt_unknown;age_ms=300\n"
    "get_items=cli_inventory_shape_only_no_delta;age_ms=5000\n"
    + ab.END + "\n"
)
BASE = "MabiAuto synthetic runtime error\nDetail: unexpected work status\n"
RUNTIME_NAME = "runtime_20261018_175959_001_abcdef12"


class OfflineEvidenceAbTests(unittest.TestCase):
    def test_paired_baseline_and_enriched_same_error(self):
        paired = ab.parse(BASE + BLOCK)
        self.assertEqual(paired.status, "valid")
        self.assertEqual(paired.baseline, BASE)
        self.assertEqual(paired.enriched, BASE + BLOCK)
        self.assertEqual(paired.rows, (
            ("status", "cli_connected"),
            ("get_altering_works", "cli_work_rows_completed_receipt_unknown"),
            ("get_items", "cli_inventory_shape_only_no_delta"),
        ))
        summary = ab.compare_text(BASE + BLOCK)
        self.assertEqual(summary["evidence_format"], "valid")
        self.assertEqual(summary["queries_with_validated_shape"], 3)
        self.assertEqual(summary["additional_hints"], (
            "inventory_delta_unknown_hint",
            "receipt_not_verified_hint",
        ))
        self.assertFalse(summary["ground_truth_verified"])
        self.assertFalse(summary["ai_analysis_run"])
        self.assertFalse(summary["diagnostic_accuracy_proven"])
        self.assertFalse(summary["gameplay_authorized"])

    def test_stage17_possible_removal_hints_do_not_prove_cancellation(self):
        """Two CLI count snapshots imply possible removal only, not identity."""
        block = (ab.BEGIN + "\n"
            "get_altering_works=cli_altering_possible_removed_not_completed;age_ms=300\n"
            + ab.END + "\n")
        result = ab.compare_text(BASE + block)
        self.assertEqual(result["evidence_format"], "valid")
        self.assertIn("work_removal_possible_not_completion_hint",
                      result["additional_hints"])
        self.assertFalse(result["ground_truth_verified"])
        self.assertFalse(result["diagnostic_accuracy_proven"])
        self.assertFalse(result["gameplay_authorized"])
        after = (ab.BEGIN + "\n"
            "get_altering_works=cli_altering_possible_finished_receipt_unknown;age_ms=5000\n"
            + ab.END + "\n")
        result2 = ab.compare_text(BASE + after)
        self.assertEqual(result2["evidence_format"], "valid")
        self.assertIn("work_finished_possible_receipt_unknown_hint",
                      result2["additional_hints"])
        self.assertFalse(result2["ground_truth_verified"])
        # Never allow an invented confirmed-cancel/receipt label.
        fabricated = (ab.BEGIN + "\n"
            "get_altering_works=cli_altering_cancelled_confirmed;age_ms=300\n"
            + ab.END + "\n")
        self.assertEqual(ab.parse(BASE + fabricated).status, "invalid")

    def test_stage18_cross_feature_triage_is_also_removed_from_baseline(self):
        triage = (ab.TRIAGE_BEGIN + "\n"
                  "area=altering\n"
                  "coverage=domain_cli_hint_only\n"
                  "hint=possible_work_removal_not_completion\n"
                  "connectivity=cli_timeout_not_disconnect\n"
                  "relevant_facts=1\n"
                  "verified_cause=false;gameplay_authorized=false;repair_authorized=false\n"
                  + ab.TRIAGE_END + "\n")
        pair = ab.parse(BASE + BLOCK + triage)
        self.assertEqual(pair.status, "valid")
        self.assertEqual(pair.baseline, BASE)
        self.assertEqual(pair.enriched, BASE + BLOCK + triage)
        report = ab.compare_text(BASE + BLOCK + triage)
        self.assertFalse(report["ground_truth_verified"])
        self.assertFalse(report["diagnostic_accuracy_proven"])
        self.assertFalse(report["ai_analysis_run"])
        self.assertFalse(report["gameplay_authorized"])
        poisoned = triage.replace("repair_authorized=false", "repair_authorized=true")
        self.assertEqual(ab.parse(BASE + BLOCK + poisoned).status, "invalid")
        leaked = triage.replace("area=altering", "area=PRIVATE_CHARACTER")
        self.assertEqual(ab.parse(BASE + BLOCK + leaked).status, "invalid")
        self.assertEqual(ab.parse(BASE + BLOCK + triage + triage).status, "invalid")
        self.assertEqual(ab.parse(BASE + BLOCK + triage.replace(
            "BEGIN COMMON INCIDENT TRIAGE V1",
            "BEGIN COMMON INCIDENT TRIAGE V2")).status, "invalid")

    def test_heuristic_drift_is_flagged_not_counted_as_accuracy_gain(self):
        # The unchanged error_memory heuristic can be biased by the
        # annotation's footer mentioning "receipt"; this is NOT an
        # accurate root-cause determination.
        summary = ab.compare_text(BASE + BLOCK)
        self.assertNotEqual(summary["baseline_heuristic_category"],
                            summary["enriched_heuristic_category"])
        self.assertTrue(summary["heuristic_category_changed"])
        self.assertFalse(summary["diagnostic_accuracy_proven"])
        self.assertFalse(summary["ground_truth_verified"])

    def test_existing_ai_prompt_same_limits_in_both_arm_simulations(self):
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / RUNTIME_NAME
            folder.mkdir()
            source = folder / "error.txt"
            source.write_text(BASE, encoding="utf-8")
            baseline = agent.context_for(folder)
            source.write_text(BASE + BLOCK, encoding="utf-8")
            enhanced = agent.context_for(folder)
            self.assertIn("Detail: unexpected work status", baseline)
            self.assertIn("Detail: unexpected work status", enhanced)
            self.assertNotIn("cli_work_rows_completed_receipt_unknown", baseline)
            self.assertIn("cli_work_rows_completed_receipt_unknown", enhanced)
            allowed = {"allowed_change_prefixes": ["source/FishingAutomation/"]}
            prompt_before = agent.prompt_for(baseline, allowed)
            prompt_after = agent.prompt_for(enhanced, allowed)
            for p in (prompt_before, prompt_after):
                self.assertIn("<untrusted-log>", p)
                self.assertIn("Never run git commit, git push, merge", p)
                self.assertIn("The outer Python host will verify", p)
                self.assertIn("Do not claim a live-game test", p)
            self.assertNotEqual(prompt_before, prompt_after)

    def test_absent_and_invalid_not_fabricated(self):
        self.assertEqual(ab.parse(BASE).status, "absent")
        self.assertEqual(ab.parse(BASE + ab.BEGIN + "\nstatus=cli_connected;age_ms=12\n")
                         .status, "invalid")
        bad_rows = (
            "status=cli_connected;age_ms=5001",  # > 5 sec
            "get_my_info=cli_connected;age_ms=10",  # private query
            "execute_altering=cli_connected;age_ms=1",  # input/action
            "status=PRIVATE_USER_TOKEN;age_ms=0",  # raw/unverified status
            "status=cli_connected;age_ms=-1",
            "status=cli_connected;age_ms=0001",
            "get_activity=cli_altering_work_running;age_ms=1", # mismatched
        )
        for row in bad_rows:
            with self.subTest(row=row):
                bad = BASE + ab.BEGIN + "\n" + row + "\n" + ab.END + "\n"
                checked = ab.parse(bad)
                self.assertEqual(checked.status, "invalid")
                self.assertEqual(checked.baseline, bad)
                self.assertEqual(checked.rows, ())
        duplicated = BASE + BLOCK + BLOCK
        self.assertEqual(ab.parse(duplicated).status, "invalid")
        out_of_order = (
            BASE + ab.BEGIN + "\n"
            "get_items=cli_inventory_shape_only_no_delta;age_ms=1\n"
            "status=cli_connected;age_ms=1\n" + ab.END + "\n")
        self.assertEqual(ab.parse(out_of_order).status, "invalid")
        self.assertEqual(ab.parse(BASE + ab.END + "\n").status, "invalid")

    def test_bounded_local_root_aggregation_without_user_text_or_ids(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            first = root / RUNTIME_NAME
            first.mkdir()
            (first / "error.txt").write_text(BASE + BLOCK +
                "PRIVATE_CHARACTER_123456", encoding="utf-8")
            other = root / "runtime_20261018_180001_002_aabbccdd"
            other.mkdir()
            (other / "error.txt").write_text(BASE, encoding="utf-8")
            corrupted = root / "runtime_20261018_180005_003_00112233"
            corrupted.mkdir()
            (corrupted / "error.txt").write_text(
                BASE + ab.BEGIN + "\nstatus=TOKEN;age_ms=0\n" +
                ab.END, encoding="utf-8")
            ignored = root / "unapproved_sidecar"
            ignored.mkdir()
            (ignored / "error.txt").write_text(
                "PRIVATE_PASS_HIDDEN", encoding="utf-8")
            report = ab.summarize(root)
            c = report["counts"]
            self.assertEqual(c["runtime_folders"], 3)
            self.assertEqual(c["readable_reports"], 3)
            self.assertEqual(c["valid_reports"], 1)
            self.assertEqual(c["invalid_reports"], 1)
            self.assertEqual(c["absent_reports"], 1)
            self.assertEqual(c["validated_query_shapes"], 3)
            self.assertEqual(c["potential_heuristic_label_bias"], 1)
            self.assertEqual(report["hint_counts"]["receipt_not_verified_hint"], 1)
            self.assertFalse(report["causal_improvement_measured"])
            self.assertFalse(report["automatic_recovery_enabled"])
            payload = json.dumps(report, ensure_ascii=False)
            for token in ["PRIVATE", "abcdef12", "aabbccdd", "00112233",
                          str(root), "execute_altering", "TOKEN"]:
                self.assertNotIn(token, payload)

    def test_reject_symlink_and_oversize_files(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            oversized = root / RUNTIME_NAME
            oversized.mkdir()
            (oversized / "error.txt").write_bytes(b"Z" * (ab.MAX_INPUT + 1))
            report = ab.summarize(root)
            self.assertEqual(report["counts"]["skipped_reports"], 1)
            self.assertEqual(report["counts"]["readable_reports"], 0)
            target = root / "runtime_20261018_180001_002_aabbccdd"
            target.mkdir()
            secret = Path(tmp) / "secret.txt"
            secret.write_text(BASE + BLOCK, encoding="utf-8")
            try:
                (target / "error.txt").symlink_to(secret)
            except (OSError, NotImplementedError):
                pass  # Windows runner may prohibit symlink creation.
            else:
                report = ab.summarize(root)
                self.assertEqual(report["counts"]["skipped_reports"], 2)
                self.assertEqual(report["counts"]["readable_reports"], 0)

    def test_reject_large_directories_and_do_not_run_live_codex(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            for i in range(ab.MAX_FOLDERS + 1):
                (root / f"runtime_20261018_180001_002_{i:08x}").mkdir()
            report = ab.summarize(root)
            self.assertEqual(report["status"], "folder_limit_exceeded")
            self.assertEqual(report["counts"]["readable_reports"], 0)
            self.assertEqual(report["codex_runs"], 0)
            self.assertEqual(report["game_runs"], 0)
            self.assertEqual(report["sample"], "local_offline_or_synthetic")


if __name__ == "__main__":
    unittest.main()
