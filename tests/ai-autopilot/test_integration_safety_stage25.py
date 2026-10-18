"""Stage25 final integration contract tests: synthetic local data ONLY.

Runs Stage21 -> Stage22 -> Stage23 -> Stage24 AND the Stage25 source gate.
Never invokes Codex, game, live CLI, packet capture, Git, or deployment.
"""
from __future__ import annotations

import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
AIDIR = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AIDIR))

import autopilot
import incident_coverage_audit as coverage
import incident_priority_review as priority
import incident_review_context as bridge
import paired_prompt_ab_stage24 as paired
import integration_safety_gate_stage25 as gate
from test_incident_coverage_stage21 import report, cli, screen, triage, write


class FinalIntegrationStage25Tests(unittest.TestCase):
    def sample(self, tmp):
        root = Path(tmp) / "runtime-reports"
        root.mkdir()
        modes = [
            ("채집", cli("get_activity=cli_gathering_in_progress;age_ms=150") +
             screen("gathering", "gathering_stop_control_double_checked",
                    "compatible_gathering_hints_only") + triage("gathering")),
            ("가공", cli("get_altering_works=cli_altering_work_running;age_ms=40") +
             screen("altering", "altering_free_action_guard_passed")),
            ("제작", screen("crafting", "crafting_result_popup_guard_passed")),
            ("낚시", screen("fishing", "fishing_hook_ready_two_frames")),
            ("어비스", screen("abyss", "abyss_clear_pair_guard_passed")),
            ("던전", screen("dungeon", "dungeon_retry_guard_passed")),
            ("페카 심층", screen("dungeon", "dungeon_retry_guard_passed")),
            ("스케줄러", ""),
        ]
        folders = []
        for idx, (mode, evidence) in enumerate(modes, start=1):
            folders.append(write(root, idx, report(mode, "PRIVATE_SENSITIVE") + evidence))
        return root, folders

    def test_full_cross_feature_synthetic_pipeline_read_only(self):
        with tempfile.TemporaryDirectory() as tmp:
            root, folders = self.sample(tmp)
            frozen = {p: (p / "error.txt").read_bytes() for p in folders}
            with (mock.patch.object(autopilot, "exec_codex",
                                    side_effect=AssertionError("Codex must not run")),
                  mock.patch.object(autopilot, "write_json",
                                    side_effect=AssertionError("AI result files forbidden")),
                  mock.patch.object(autopilot.subprocess, "run",
                                    side_effect=AssertionError("no external process"))):
                c21 = coverage.summarize(root)
                q22 = priority.prioritize(c21)
                advisory = bridge.for_runtime_error(folders[0], root)
                ab24 = paired.evaluate(root)
            self.assertEqual(c21["status"], "offline_complete")
            self.assertEqual(c21["counts"]["readable_reports"], 8)
            self.assertEqual(c21["counts"]["supported_gathering_comparison"], 1)
            self.assertEqual(c21["counts"]["with_screen_annotation"], 7)
            self.assertEqual(c21["counts"]["both_annotations"], 2)
            self.assertEqual(c21["counts"]["screen_only"], 5)
            self.assertEqual(c21["counts"]["neither_annotation"], 1)
            self.assertEqual(c21["counts"]["unsupported_semantic_comparison"], 1)
            self.assertEqual(set(c21["by_mode"]), set(coverage.MODE_AREAS))
            self.assertEqual(q22["status"], "review_candidates_available")
            self.assertEqual(
                [it["category"] for it in q22["review_items"]],
                ["missing_both_annotations", "screen_without_cli",
                 "noncomparable_dual_hints"])
            self.assertIn("scope=global_aggregate_not_this_incident", advisory)
            self.assertTrue(paired._valid_advisory(advisory))
            self.assertEqual(ab24["paired_prompt_inputs"], 8)
            self.assertEqual(ab24["counts"]["paired_with_advisory"], 8)
            self.assertEqual(ab24["counts"]["prompt_content_changed_as_expected"], 8)
            self.assertEqual(ab24["counts"]["unexpected_prompt_difference"], 0)
            self.assertEqual(ab24["model_responses_collected"], 0)
            self.assertEqual(ab24["codex_executions"], 0)
            self.assertEqual(ab24["game_runs"], 0)
            self.assertFalse(ab24["automatic_repair_authorized"])
            self.assertFalse(q22["repair_authorized"])
            self.assertFalse(c21["ground_truth_verified"])
            self.assertEqual(c21["diagnostic_accuracy_change"], "not_measured")
            for folder in folders:
                self.assertEqual((folder / "error.txt").read_bytes(), frozen[folder])
                self.assertEqual(
                    sorted(p.name for p in folder.iterdir()), ["error.txt"],
                    "no generated reports or hidden AI job files")
            wire = json.dumps([c21, q22, ab24], ensure_ascii=False)
            for secret in ("PRIVATE_SENSITIVE", "runtime_20261018",
                           str(root), "error.txt", "sk-", "authorization:"):
                self.assertNotIn(secret, wire)
            self.assertTrue(gate.inspect(ROOT)["stage26_review_candidate"])

    def test_one_corrupted_record_quarantined_but_no_false_game_state_conflict(self):
        with tempfile.TemporaryDirectory() as tmp:
            root, folders = self.sample(tmp)
            error = folders[1] / "error.txt"
            error.write_text(
                error.read_text(encoding="utf-8").replace(
                    "repair_authorized=false", "repair_authorized=true"),
                encoding="utf-8"
            )
            summary = coverage.summarize(root)
            self.assertEqual(summary["counts"]["invalid_screen_annotation"], 1)
            self.assertEqual(summary["counts"]["reports_with_invalid_annotations"], 1)
            self.assertEqual(summary["counts"]["with_screen_annotation"], 6)
            self.assertEqual(summary["counts"]["both_annotations"], 1)
            self.assertEqual(summary["verified_gameplay_conflicts"], "not_measurable")
            out = priority.prioritize(summary)
            self.assertEqual(out["review_items"][0]["category"],
                             "invalid_annotations")
            self.assertFalse(out["repair_authorized"])
            self.assertTrue(bridge.for_runtime_error(folders[0], root))
            # Syntax-valid global reference never authorizes a corrupted event.
            self.assertEqual(bridge.for_runtime_error(folders[1], root) != "", True)

    def test_source_gate_passes_current_code_and_reports_no_release_authority(self):
        r = gate.inspect(ROOT)
        self.assertEqual(r["status"], "source_contracts_passed_review_required")
        self.assertEqual(r["checks_total"], len(gate.CHECKS))
        self.assertEqual(r["checks_passed"], len(gate.CHECKS))
        self.assertTrue(all(r["checks"].values()))
        self.assertEqual(r["live_game_validation"], "not_performed")
        self.assertEqual(r["ai_diagnostic_accuracy_change"], "not_measured")
        self.assertIs(r["ci_outcomes_measured_here"], False)
        self.assertIs(r["source_gate_is_not_release_approval"], True)
        for key in ("auto_repair_authorized", "auto_merge_authorized",
                    "deployment_authorized", "gameplay_authorized"):
            self.assertIs(r[key], False)
        for key in ("codex_executions", "network_requests", "new_game_queries",
                    "files_written"):
            self.assertEqual(r[key], 0)

    def test_every_contract_can_fail_closed_if_required_source_is_changed(self):
        src = gate._read_sources(ROOT)
        self.assertIsNotNone(src)
        self.assertTrue(all(gate._bool_checks(src).values()))
        corruptions = [
            ("project", "MABIAUTO_AI_PASSIVE_CONTEXT", "UNSAFE_AI_ALWAYS_ON",
             "production_passive_build_guard"),
            ("watcher", "UseShellExecute = false", "UseShellExecute = true",
             "watcher_explicit_start_and_disable"),
            ("journal", "SnapshotForScreenIncident(", "SnapshotForUnverified(",
             "journal_opt_in_snapshot_only"),
            ("coverage", "root.is_symlink()", "root.is_link_fake()",
             "bounded_local_diagnostic_adapters"),
            ("priority", '"diagnostic_accuracy_change"', '"fake_accuracy"',
             "fixed_priority_and_no_truth_claim"),
            ("agent", "<untrusted-log>", "<trusted-command>",
             "codex_untrusted_prompt_fence"),
            ("agent", "No auto merge/deploy: live-game validation and human approval still required",
             "AUTO_MERGE_NOW", "existing_ai_isolation_test_review_gates"),
            ("agent", 'folders = all_evidence_folders(c)  # newest first: never reverse',
             "folders = list(reversed(all_evidence_folders(c)))",
             "watcher_queue_no_diagnostic_reordering"),
            ("ab", '"codex_executions": 0', '"codex_executions": 1',
             "offline_ab_no_model_or_game_calls"),
            ("ci", "test_paired_prompt_ab_stage24.py",
             "test_paired_prompt_bypass.py", "ci_regression_registration"),
        ]
        for name, original, mutated, expected_flag in corruptions:
            with self.subTest(name=name, gate=expected_flag):
                self.assertIn(original, src[name])
                altered = dict(src)
                altered[name] = altered[name].replace(original, mutated, 1)
                flags = gate._bool_checks(altered)
                self.assertFalse(flags[expected_flag],
                                 "critical contract not blocked")
        self.assertFalse(any(gate._bool_checks({"agent": "bad"}).values()))

    def test_missing_symlink_and_corrupt_source_checkout_blocks_without_logging(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            r = gate.inspect(root)
            self.assertEqual(r["status"], "integration_contract_blocked")
            self.assertEqual(r["checks_passed"], 0)
            self.assertFalse(r["stage26_review_candidate"])
            self.assertFalse(r["deployment_authorized"])
            self.assertFalse(r["auto_merge_authorized"])
            # Root path is not included, even after failing to read.
            self.assertNotIn(str(root), json.dumps(r))

    def test_no_extra_llm_runner_or_silent_deployment_from_contract_module(self):
        import ast
        tree = ast.parse((AIDIR / "integration_safety_gate_stage25.py")
                         .read_text(encoding="utf-8"))
        import_names = set()
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                import_names.update(alias.name.split(".")[0] for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module:
                import_names.add(node.module.split(".")[0])
        self.assertTrue(import_names <=
                        {"__future__", "ast", "json", "pathlib", "xml"})
        assert "subprocess" not in import_names
        assert "requests" not in import_names
        assert "socket" not in import_names


if __name__ == "__main__":
    unittest.main()
