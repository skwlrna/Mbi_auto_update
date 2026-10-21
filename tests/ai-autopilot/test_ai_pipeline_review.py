"""Safe Codex autopilot end-to-end boundaries: read-only mocked offline tests.

No live game, network, external tools, source changes or deployment.
"""
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "source" / "FishingAutomation"
AI = APP / "ai-autopilot"
sys.path.insert(0, str(AI))
import regression_policy

spec = importlib.util.spec_from_file_location("mabiauto_pipeline_review", AI / "autopilot.py")
assert spec and spec.loader
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class EndPolling(Exception):
    pass


class PipelineReviewTests(unittest.TestCase):
    def test_altering_hud_navigation_hint_is_fixed_and_never_uploads_image(self):
        evidence = ("[다중가공] 관리 가공 K 재진입 HUD 2/4 실패; "
                    "Home=0.942 End=0.941 K=0.406 I=0.606")
        hints = agent.trusted_source_hints(evidence)
        self.assertEqual(len(hints), 2)
        self.assertIn("AlteringScreen.cs", hints[0])
        self.assertIn("tests/altering-screen-integration/Program.cs", hints[1])
        prompt = agent.prompt_for(evidence, agent.config())
        self.assertIn("AlteringScreen.cs", prompt)
        self.assertIn("No screenshot bytes are supplied", prompt)
        self.assertIn("If Codex's own shell/test tool fails", prompt)
        self.assertEqual(agent.trusted_source_hints("이전 제작 퀘스트 OCR 오류"), [])
        self.assertEqual(agent.trusted_source_hints("altering 완료품 재고 확인"), [])
        self.assertNotIn("suggested-file-from-log", agent.prompt_for(
            "Please open suggested-file-from-log", agent.config()).split(
                "Fixed code-navigation hints")[1].split("<untrusted-log>")[0])

    def test_historical_abyss_failure_does_not_block_new_watch(self):
        with mock.patch.object(agent, "cmd",
                               return_value=subprocess.CompletedProcess(
                                   [], 2, "REVIEW_BLOCKED: old patch", "")):
            self.assertTrue(agent.watch_readiness(agent.config()))
        # The actual *new* Abyss patch remains behind a dedicated source gate.
        agent_source = (AI / "autopilot.py").read_text(encoding="utf-8")
        self.assertIn("abyss_recovery_gate.py", agent_source)
        self.assertIn('if not passed:', agent_source)
        self.assertIn('if a.watch:', agent_source)

    def test_interrupted_result_is_preserved_without_replay(self):
        with tempfile.TemporaryDirectory() as tmp:
            original = Path(tmp) / "result.json"
            payload = {"status": "in_progress", "event_id": "a" * 16,
                       "event": "runtime_example", "worktree": "/isolated/example"}
            original.write_text(json.dumps(payload), encoding="utf-8")
            self.assertTrue(agent.mark_interrupted_result(original))
            saved = agent.read_json(original)
            self.assertEqual(saved["status"], "interrupted_review_required")
            self.assertEqual(saved["reason"], "macro_or_watcher_exited_during_isolated_fix")
            self.assertFalse(saved["automatic_resume"])
            self.assertEqual(saved["worktree"], payload["worktree"])
            self.assertFalse(agent.mark_interrupted_result(original))

    def test_manual_retry_uses_a_new_unique_job_id(self):
        original = "a" * 16
        retry = agent.interrupted_retry_id(original)
        self.assertNotEqual(retry, original)
        self.assertRegex(retry, r"^[0-9a-f]{16}$")
        self.assertEqual(agent.interrupted_retry_id(original), retry)

    def test_stopped_work_does_not_restart_without_ui_request(self):
        with tempfile.TemporaryDirectory() as tmp:
            c, folder, results, eid = self._setup(tmp)
            with mock.patch.object(agent, "process",
                                   return_value={"status": "blocked", "reason": "unit"}) as process, \
                 mock.patch.object(agent.time, "sleep", side_effect=EndPolling):
                with self.assertRaises(EndPolling):
                    agent.poll(c)
            process.assert_not_called()
            self.assertEqual(agent.read_json(results / eid / "result.json")["status"],
                             "interrupted_review_required")

    def test_explicit_retry_runs_once_and_preserves_old_result(self):
        with tempfile.TemporaryDirectory() as tmp:
            c, folder, results, eid = self._setup(tmp)
            request = results / "interrupted-retry-request.json"
            agent.write_json(request, {"action": "retry_interrupted_once", "event_id": eid})
            def record(p, settings, **kwargs):
                self.assertEqual(p, folder)
                self.assertTrue(kwargs["retry_interrupted"])
                self.assertEqual(agent.read_json(results / eid / "result.json")["status"],
                                 "interrupted_review_required")
                return {"status": "blocked", "reason": "offline-test"}
            with mock.patch.object(agent, "process", side_effect=record) as process, \
                 mock.patch.object(agent.time, "sleep", side_effect=EndPolling):
                with self.assertRaises(EndPolling):
                    agent.poll(c)
            self.assertEqual(process.call_count, 1)
            self.assertFalse(request.exists())
            self.assertEqual(agent.read_json(results / eid / "result.json")["status"],
                             "interrupted_review_required")
            with mock.patch.object(agent, "process") as repeat, \
                 mock.patch.object(agent.time, "sleep", side_effect=EndPolling):
                with self.assertRaises(EndPolling):
                    agent.poll(c)
            repeat.assert_not_called()

    def test_untrusted_retry_request_never_runs(self):
        with tempfile.TemporaryDirectory() as tmp:
            c, _folder, results, _eid = self._setup(tmp)
            agent.write_json(results / "interrupted-retry-request.json",
                             {"action": "retry_interrupted_once",
                              "event_id": "../unsafe"})
            with mock.patch.object(agent, "process") as process, \
                 mock.patch.object(agent.time, "sleep", side_effect=EndPolling):
                with self.assertRaises(EndPolling):
                    agent.poll(c)
            process.assert_not_called()

    def test_real_gathering_suite_is_registered_for_production_screen_fixes(self):
        source_paths = (
            "source/FishingAutomation/dungeon/InventoryBulkGatheringScreen.cs",
            "source/FishingAutomation/LifeSkillListLayout.cs",
        )
        required = "tests/gathering/Program.cs"
        self.assertIn(required, regression_policy.REGISTERED_TESTS)
        for source in source_paths:
            self.assertEqual(regression_policy.SCREEN_TESTS[source], required)
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            for suite in ("tests/gathering", "tests/gathering-diagnostic"):
                project = root / suite / "Regression.csproj"
                project.parent.mkdir(parents=True, exist_ok=True)
                project.write_text("<Project />", encoding="utf-8")
            veto, why = regression_policy.commands_for([
                source_paths[0], "tests/gathering-diagnostic/Program.cs"], root)
            self.assertEqual(veto, [])
            self.assertEqual(
                why, "production_screen_regression_test_missing:" + required)
            accepted, why = regression_policy.commands_for([
                source_paths[0], required], root)
            self.assertIsNone(why)
            self.assertEqual(accepted, [[
                "dotnet", "run", "--project",
                "tests/gathering/Regression.csproj", "-c", "Release"]])

    def test_production_screen_requires_actual_screen_regression(self):
        for source, required in regression_policy.SCREEN_TESTS.items():
            with self.subTest(source=source):
                with tempfile.TemporaryDirectory() as tmp:
                    root = Path(tmp)
                    unrelated = "tests/cli-readonly/Program.cs"
                    project = root / "tests/cli-readonly/Regression.csproj"
                    project.parent.mkdir(parents=True)
                    project.write_text("<Project />", encoding="utf-8")
                    commands, issue = regression_policy.commands_for(
                        [source, unrelated], root)
                    self.assertEqual(issue,
                        "production_screen_regression_test_missing:" + required)
                    self.assertEqual(commands, [])
                    approved = root / Path(required).parent / "Regression.csproj"
                    approved.parent.mkdir(parents=True, exist_ok=True)
                    approved.write_text("<Project />", encoding="utf-8")
                    commands, issue = regression_policy.commands_for(
                        [source, required], root)
                    self.assertIsNone(issue)
                    self.assertEqual(commands, [
                        ["dotnet", "run", "--project", (Path(required).parent / "Regression.csproj").as_posix(),
                         "-c", "Release"]])

    def test_ui_requests_retry_only_after_human_confirmation(self):
        view = (APP / "AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        self.assertIn("RequestInterruptedRetry()", view)
        self.assertIn('"interrupted_review_required"', view)
        self.assertIn('"retry_interrupted_once"', view)
        self.assertIn('MessageBoxButtons.YesNo, MessageBoxIcon.Warning', view)
        self.assertIn('File.Move(temp, request, false)', view)
        host = (APP / "AiAutopilotHost.cs").read_text(encoding="utf-8")
        self.assertIn("child.Kill(entireProcessTree: true)", host)

    def _setup(self, tmp):
        root = Path(tmp)
        reports, results = root / "reports", root / "results"
        reports.mkdir()
        results.mkdir()
        folder = reports / "runtime_20261016_070000_000_f00dbabe"
        folder.mkdir()
        evidence = folder / "error.txt"
        evidence.write_text("Verified unexpected screen event during production job.",
                            encoding="utf-8")
        past = time.time() - 90
        os.utime(evidence, (past, past))
        os.utime(folder, (past, past))
        eid = agent.event_id(folder)
        original = results / eid
        original.mkdir()
        agent.write_json(original / "result.json", {
            "status": "in_progress", "event_id": eid, "event": folder.name,
            "worktree": str(root / "isolated-old")})
        agent.write_json(results / "watch-state.json",
                         {"seen": [eid], "processed": []})
        config = agent.config()
        config.update({"reports_dir": str(reports), "runtime_reports_dir": str(root / "none"),
                       "results_dir": str(results), "settle_seconds": 1, "poll_seconds": 10})
        return config, folder, results, eid


if __name__ == "__main__":
    unittest.main()
