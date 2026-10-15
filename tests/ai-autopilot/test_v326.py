"""V3.2.6: supervised AI, executable regression gates, and evidence safeguards."""
from pathlib import Path
import importlib.util
import json
import sys
import tempfile
import unittest
from unittest import mock

BASE = Path(__file__).resolve().parents[2]
AIPATH = BASE / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AIPATH))
import supervised_codex
import regression_policy

spec = importlib.util.spec_from_file_location("agent_under_test_v326", AIPATH / "autopilot.py")
assert spec and spec.loader
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class WatchdogAndRegressionTests(unittest.TestCase):
    def test_supervisor_runs_and_drains_normal_output(self):
        with tempfile.TemporaryDirectory() as temp:
            p = Path(temp)
            status, out, err, issue = supervised_codex.supervised_run(
                [sys.executable, "-u", "-c",
                 "import sys; print('turn.completed'); print('debug', file=sys.stderr)"],
                p, "", p / "events.jsonl", p / "stderr.txt",
                idle_seconds=60, hard_seconds=90, heartbeat_seconds=15)
            self.assertEqual((status, issue), (0, None))
            self.assertIn("turn.completed", out)
            self.assertIn("debug", err)
            self.assertTrue((p / "events.jsonl").is_file())

    def test_stalled_codex_cannot_restart_automatically(self):
        with tempfile.TemporaryDirectory() as temp:
            p = Path(temp)
            c = agent.config()
            c["codex_max_retries"] = 2
            with mock.patch.object(agent.shutil, "which", return_value="codex.exe"), \
                 mock.patch.object(agent, "require_isolated_ai_mode"), \
                 mock.patch.object(agent, "supervised_run", return_value=(-1, "", "", "codex_stalled")) as run:
                ok, reason = agent.exec_codex(p, "test", p, c)
            self.assertFalse(ok)
            self.assertEqual(reason, "codex_stalled")
            self.assertEqual(run.call_count, 1)

    def test_autopilot_has_independent_idle_and_total_deadlines(self):
        c = agent.config()
        self.assertLess(int(c["codex_stall_seconds"]), int(c["codex_timeout_seconds"]))
        self.assertTrue(c["require_regression_test"])
        s = (AIPATH / "supervised_codex.py").read_text(encoding="utf-8")
        self.assertIn('issue = "codex_stalled"', s)
        self.assertIn('terminate_tree(proc)', s)

    def test_missing_or_unregistered_test_is_review_only(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            self.assertEqual(regression_policy.commands_for(
                ["source/FishingAutomation/dungeon/AlteringScreen.cs"], base)[1],
                "regression_test_missing")
            self.assertEqual(regression_policy.commands_for(
                ["source/FishingAutomation/dungeon/AlteringScreen.cs",
                 "tests/unapproved/Program.cs"], base)[1],
                "regression_test_suite_not_registered")

    def test_registered_test_runs_without_arbitrary_script(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = root / "tests" / "altering" / "Regression.csproj"
            project.parent.mkdir(parents=True)
            project.write_text("<Project/>")
            commands, problem = regression_policy.commands_for(
                ["source/FishingAutomation/dungeon/AlteringScreen.cs",
                 "tests/altering/Program.cs"], root)
            self.assertIsNone(problem)
            self.assertEqual(commands, [["dotnet", "run", "--project",
                                          "tests/altering/Regression.csproj", "-c", "Release"]])

    def test_visual_evidence_is_local_and_bounded(self):
        runtime = (BASE / "source" / "FishingAutomation" / "dungeon" /
                   "ProductionUiRuntime.cs").read_text(encoding="utf-8")
        buffer = (BASE / "source" / "FishingAutomation" / "dungeon" /
                  "ProductionEvidenceBuffer.cs").read_text(encoding="utf-8")
        self.assertIn('_evidence.Observe(frame)', runtime)
        self.assertIn('string evidence = _evidence.Save(message)', runtime)
        self.assertIn('private const int MaxFrames = 7;', buffer)
        self.assertIn('sampled_pixel_change=', buffer)
        self.assertIn('ImageFormat.Png', buffer)
        self.assertNotIn('HttpClient', buffer)
        self.assertNotIn('PutFileAsync(', buffer)
        self.assertNotIn('new HttpClient(', buffer)

    def test_visual_context_requires_fixed_ancestor_and_home_directory(self):
        source = (AIPATH / "autopilot.py").read_text(encoding="utf-8")
        self.assertIn('candidate.parent.parent.name != "visual-evidence"', source)
        self.assertIn('candidate.is_relative_to(home)', source)
        self.assertIn('NO SCREENSHOT BYTES SHARED', source)


if __name__ == "__main__":
    unittest.main()
