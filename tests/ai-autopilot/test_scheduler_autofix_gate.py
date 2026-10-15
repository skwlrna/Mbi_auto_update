"""Scheduler AI repair gate: deterministic offline tests, no Codex/game/network."""
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import regression_policy
import error_memory


class SchedulerAutofixGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.tree = Path(self.temp.name)
        for name in ("tests/combined-scheduler/Regression.csproj",
                     "tests/altering/Regression.csproj"):
            target = self.tree / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("<Project />", encoding="utf-8")

    def selection(self, *paths):
        return regression_policy.commands_for(list(paths), self.tree)

    def test_scheduler_edit_rejects_unrelated_registered_test_only(self):
        cmds, reason = self.selection(
            "source/FishingAutomation/MainForm.SchedulerRunner.cs",
            "tests/altering/Program.cs")
        self.assertEqual(cmds, [])
        self.assertEqual(reason, "scheduler_regression_test_missing")

    def test_scheduler_resume_requires_combined_suite(self):
        self.assertTrue(regression_policy.changes_scheduler_source(
            "source/FishingAutomation/MainForm.SchedulerResume.cs"))
        self.assertEqual(self.selection(
            "source/FishingAutomation/MainForm.SchedulerResume.cs",
            "tests/altering/Program.cs")[1], "scheduler_regression_test_missing")

    def test_scheduler_source_names_include_progress_and_retry(self):
        paths = [
            "source/FishingAutomation/SchedulerRunMonitor.cs",
            "source/FishingAutomation/SchedulerReadOnlyRetry.cs",
            "source/FishingAutomation/MultiAlteringDependencyScheduler.cs",
            "source/FishingAutomation/dungeon/ScenarioEngine.AbyssSchedulerExit.cs",
        ]
        for source in paths:
            with self.subTest(source=source):
                self.assertEqual(self.selection(source, "tests/altering/Program.cs")[1],
                                 "scheduler_regression_test_missing")

    def test_combined_scheduler_suite_selected_for_scheduler_patch(self):
        cmds, reason = self.selection(
            "source/FishingAutomation/MainForm.SchedulerRunner.cs",
            "tests/combined-scheduler/Program.cs")
        self.assertIsNone(reason)
        self.assertEqual(cmds, [[
            "dotnet", "run", "--project", "tests/combined-scheduler/Regression.csproj",
            "-c", "Release"]])

    def test_additional_registered_tests_run_alongside_scheduler(self):
        cmds, reason = self.selection(
            "source/FishingAutomation/SchedulerReadOnlyRetry.cs",
            "tests/altering/Program.cs", "tests/combined-scheduler/Program.cs")
        self.assertIsNone(reason)
        self.assertEqual(len(cmds), 2)
        self.assertTrue(any("tests/combined-scheduler/Regression.csproj" in cmd
                            for cmd in cmds))

    def test_unrelated_fix_retains_prior_allowlist_behavior(self):
        self.assertEqual(self.selection(
            "source/FishingAutomation/AlteringReceiptPolicy.cs",
            "tests/altering/Program.cs")[1], None)

    def test_missing_or_unregistered_or_missing_project_still_blocked(self):
        self.assertEqual(self.selection(
            "source/FishingAutomation/MainForm.SchedulerRunner.cs")[1],
            "regression_test_missing")
        self.assertEqual(self.selection(
            "source/FishingAutomation/MainForm.SchedulerRunner.cs",
            "tests/nonexistent/Program.cs")[1],
            "regression_test_suite_not_registered")
        (self.tree / "tests/combined-scheduler/Regression.csproj").unlink()
        self.assertEqual(self.selection(
            "source/FishingAutomation/MainForm.SchedulerRunner.cs",
            "tests/combined-scheduler/Program.cs")[1],
            "regression_test_project_missing")

    def test_test_only_change_does_not_claim_production_fix(self):
        self.assertEqual(self.selection(
            "tests/combined-scheduler/Program.cs")[1],
            "production_source_patch_missing")

    def test_codex_prompt_requires_scheduler_specific_positive_and_veto(self):
        agent = (AI / "autopilot.py").read_text(encoding="utf-8")
        self.assertIn("tests/combined-scheduler/Program.cs with real positive AND veto cases",
                      agent)
        self.assertIn("No test fabrication or safety bypass", agent)
        self.assertIn("Never run git commit, git push, merge", agent)

    def test_scheduler_tag_prioritized_without_promoting_to_verified(self):
        context = ("[스케줄러] 수령 전 단계 전환 예외 · CLI wait expired "
                   "and receipt checkpoint mismatch " * 3)
        cls = error_memory.classify(
            context, ["source/FishingAutomation/MainForm.SchedulerRunner.cs"])
        self.assertEqual(cls["suspected_category"], "scheduler_transition")
        self.assertEqual(cls["candidate_scope"], "scheduler_logic")
        self.assertEqual(cls["cause_confidence"], "heuristic_not_verified")
        self.assertEqual(error_memory.classify(
            "receipt journal mismatch", ["source/FishingAutomation/AlteringReceiptPolicy.cs"]
            )["suspected_category"], "receipt_state")

    def test_scheduler_memory_is_hashes_only_and_unverified(self):
        context = ("[스케줄러] resume handoff receipt journal mismatch "
                   "token SECRET-AUTOFIX-NEVER-STORE " * 4)
        eid = "f" * 16
        error_memory.record_attempt(self.tree, eid, context, {
            "status": "tests_passed_review_required",
            "changed_files": ["source/FishingAutomation/MainForm.SchedulerRunner.cs"],
        })
        stored = (self.tree / "error-memory.json").read_text(encoding="utf-8")
        self.assertNotIn("SECRET-AUTOFIX-NEVER-STORE", stored)
        record = json.loads(stored)["entries"][0]
        self.assertEqual(record["classification"]["suspected_category"],
                         "scheduler_transition")
        self.assertFalse(record["game_validated"])
        self.assertEqual(error_memory.read_user_feedback(self.tree, eid), "unverified")


if __name__ == "__main__":
    unittest.main()
