"""Stage22: synthetic-only review prioritization, no AI/CLI/network/game run."""
from pathlib import Path
import json
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "source" / "FishingAutomation" / "ai-autopilot"))
import incident_coverage_audit as audit
import incident_priority_review as priority
from test_incident_coverage_stage21 import cli, report, screen, triage, write


class Stage22ReviewTests(unittest.TestCase):
    def assert_no_authority(self, result):
        self.assertFalse(result["ai_analysis_run"])
        self.assertFalse(result["gameplay_authorized"])
        self.assertFalse(result["repair_authorized"])
        self.assertFalse(result["deployment_authorized"])
        self.assertEqual(result["new_cli_queries"], 0)
        self.assertEqual(result["new_screen_captures"], 0)
        self.assertEqual(result["network_requests"], 0)
        self.assertEqual(result["game_runs"], 0)
        self.assertEqual(result["actual_error_severity"], "unknown")
        self.assertEqual(result["verified_root_causes"], "not_measurable")
        self.assertEqual(result["false_positive_rate"], "not_measurable")
        self.assertEqual(result["false_negative_rate"], "not_measurable")

    def test_offline_priority_order_not_automatic_repair(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            # Invalid annotation; quarantine event's other otherwise valid signals.
            write(root, 1, report("채집", "PRIVATE_PLAYER") +
                  cli("get_activity=cli_gathering_in_progress;age_ms=100") +
                  screen().replace(
                      "repair_authorized=false", "repair_authorized=true"))
            # A mode mismatch is a document quality issue, not in-game state.
            write(root, 2, report("던전") +
                  screen("abyss", "abyss_result_guard_passed"))
            write(root, 3, report("채집") +
                  cli("get_activity=cli_gathering_in_progress;age_ms=200") +
                  screen(comparison="no_comparable_cli_state"))
            write(root, 4, report("제작"))
            write(root, 5, report("낚시") +
                  cli("status=cli_connected;age_ms=30"))
            write(root, 6, report("제작") +
                  screen("crafting", "crafting_result_popup_guard_passed"))
            write(root, 7, report("가공") +
                  cli("get_altering_works=cli_altering_work_running;age_ms=10") +
                  screen("altering", "altering_free_action_guard_passed"))
            result = priority.prioritize(audit.summarize(root))
            self.assertEqual(result["status"], "review_candidates_available")
            self.assertEqual(result["readable_reports"], 7)
            ids = [row["category"] for row in result["review_items"]]
            self.assertEqual(ids, [
                "invalid_annotations", "mode_label_mismatch",
                "self_inconsistent_annotations", "missing_both_annotations",
                "screen_without_cli", "cli_without_screen",
                "noncomparable_dual_hints",
            ])
            self.assertEqual(
                [row["tier"] for row in result["review_items"]],
                ["P1", "P1", "P2", "P2", "P3", "P3", "P3"])
            for row in result["review_items"]:
                self.assertGreater(row["occurrences"], 0)
                self.assertIn(row["next_review"],
                              [spec[2] for spec in priority.POLICY])
                self.assertEqual(
                    row["meaning"], "diagnostic_annotation_quality_only")
            self.assert_no_authority(result)
            payload = json.dumps(result, ensure_ascii=False)
            for token in ("PRIVATE", "20261018_221500", str(root),
                          "repair_authorized=true", "cli_connected;age_ms=",
                          "caught_fish", "playerName"):
                self.assertNotIn(token, payload)
            self.assertEqual(
                priority.prioritize(audit.summarize(root)), result,
                "unchanged local data should yield stable, deterministic ordering")

    def test_no_files_not_evidence_of_success(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "empty"
            root.mkdir()
            empty = priority.prioritize(audit.summarize(root))
            self.assertEqual(empty["status"], "insufficient_or_untrusted_aggregate")
            self.assertEqual(empty["review_items"], [])
            self.assertEqual(empty["readable_reports"], 0)
            self.assert_no_authority(empty)
            missing = priority.prioritize(audit.summarize(root / "missing"))
            self.assertEqual(missing["source_status"], "root_unavailable")
            self.assertEqual(missing["review_items"], [])
            self.assert_no_authority(missing)

    def test_only_supported_gathering_hints_no_priority_gap(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            write(root, 1, report() +
                  cli("get_activity=cli_gathering_in_progress;age_ms=250") +
                  screen(comparison="compatible_gathering_hints_only") +
                  triage())
            res = priority.prioritize(audit.summarize(root))
            self.assertEqual(res["status"], "no_priority_gaps_observed")
            self.assertEqual(res["review_items"], [])
            # Even paired "compatible" labels do not prove completed work.
            self.assert_no_authority(res)

    def test_noncomparable_modes_never_infer_gameplay_disagreement(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            write(root, 1, report("가공") +
                  cli("get_altering_works=cli_altering_work_running;age_ms=5") +
                  screen("altering", "altering_free_action_guard_passed"))
            res = priority.prioritize(audit.summarize(root))
            self.assertEqual(
                [x["category"] for x in res["review_items"]],
                ["noncomparable_dual_hints"])
            self.assertEqual(res["review_items"][0]["tier"], "P3")
            self.assert_no_authority(res)

    def test_reject_forged_summary_counts_and_authority(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            write(root, 1, report())
            base = audit.summarize(root)
            invalid = [
                None, [], {}, {"schema": 1},
                dict(base, gameplay_authorized=True),
                dict(base, repair_authorized=True),
                dict(base, ground_truth_verified=True),
                dict(base, new_cli_queries=1),
                dict(base, network_requests=1),
                dict(base, stage=23),
                dict(base, source="PRIVATE"),
                dict(base, diagnostic_accuracy_change="improved_99_percent"),
                dict(base, status="bogus"),
                dict(base, counts={**base["counts"], "readable_reports": -1}),
                dict(base, counts={**base["counts"], "cli_only": 42}),
                dict(base, counts={**base["counts"], "with_screen_annotation": True}),
                dict(base, counts={**base["counts"], "private_field": "PRIVATE"}),
                dict(base, by_mode={"PRIVATE_PERSON": {
                    "readable": 1, "valid_cli": 0, "valid_screen": 0, "both": 0,
                    "supported_comparison": 0, "annotation_inconsistency": 0
                }}),
                dict(base, by_mode={}),
            ]
            for value in invalid:
                with self.subTest(value=str(type(value))):
                    output = priority.prioritize(value)
                    self.assertEqual(
                        output["status"], "insufficient_or_untrusted_aggregate")
                    self.assertEqual(output["review_items"], [])
                    self.assert_no_authority(output)

    def test_root_size_limit_produces_no_priority_list(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp) / "runtime-reports"
            root.mkdir()
            for i in range(audit.ab.MAX_FOLDERS + 1):
                (root / ("runtime_20261018_221500_%03d_%08x" % (i, i))).mkdir()
            out = priority.prioritize(audit.summarize(root))
            self.assertEqual(out["source_status"], "folder_limit_exceeded")
            self.assertEqual(out["review_items"], [])
            self.assert_no_authority(out)

    def test_fixed_policy_not_gameplay_severity_or_recency_scoring(self):
        self.assertEqual(len(priority.POLICY), 10)
        self.assertLessEqual(len(priority.POLICY), priority.MAX_REVIEW_ITEMS)
        self.assertEqual(
            [row[0] for row in priority.POLICY],
            ["P1"] * 2 + ["P2"] * 3 + ["P3"] * 4 + ["P4"])
        self.assertEqual(len({x[1] for x in priority.POLICY}),
                         len(priority.POLICY))
        self.assertTrue(all(x[2].startswith("review_")
                            for x in priority.POLICY))


if __name__ == "__main__":
    unittest.main()
