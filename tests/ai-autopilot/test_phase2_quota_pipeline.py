"""Codex pipeline integration boundaries and quota reanalysis.

Offline only: all Git, Codex, CI, GitHub and game effects are mocked.
Host readiness explicitly cannot prove live Codex patching or game behavior.
"""
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

BASE = Path(__file__).resolve().parents[2]
AI = BASE / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import github_pr

spec = importlib.util.spec_from_file_location("mabiauto_phase2", AI / "autopilot.py")
assert spec and spec.loader
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class StopLoop(Exception):
    pass


class Phase2Tests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        self.repo = root / "repo"
        self.reports = root / "reports"
        self.results = root / "results"
        self.work = root / "worktrees"
        for d in (self.repo, self.reports, self.results, self.work):
            d.mkdir()
        self.config = agent.config()
        self.config.update({
            "repo": str(self.repo), "reports_dir": str(self.reports),
            "runtime_reports_dir": str(root / "local"),
            "results_dir": str(self.results), "worktrees_dir": str(self.work),
            "settle_seconds": 1, "poll_seconds": 10, "max_fix_rounds": 1,
        })
        agent.write_json(self.results / "watch-state.json", {"seen": [], "processed": []})

    def add_error(self, name="runtime_phase2_20261016_100000_001_abcd1234"):
        directory = self.reports / name
        directory.mkdir()
        entry = directory / "error.txt"
        entry.write_text("synthetic production screen retry failure for offline-only test",
                         encoding="utf-8")
        past = time.time() - 90
        os.utime(entry, (past, past))
        os.utime(directory, (past, past))
        return directory

    def cycle(self, action):
        with mock.patch.object(agent, "require_isolated_ai_mode"), \
             mock.patch.object(agent, "process", side_effect=action) as process, \
             mock.patch.object(agent.time, "sleep", side_effect=StopLoop):
            with self.assertRaises(StopLoop):
                agent.poll(self.config)
        return process

    def test_quota_retry_requires_explicit_request_after_provider_reset(self):
        folder = self.add_error()
        first = self.cycle(lambda *_a, **_k: {
            "status": "blocked", "reason": "codex_unverified: codex_quota_exhausted"})
        self.assertEqual(first.call_count, 1)
        eid = agent.event_id(folder)
        (self.results / eid).mkdir()
        agent.write_json(self.results / eid / "result.json", {
            "event": folder.name, "event_id": eid, "status": "blocked",
            "reason": "codex_unverified: codex_quota_exhausted"})
        pause = agent.read_json(self.results / "watch-state.json")["quota_pause"]
        request = self.results / "quota-retry-request.json"
        agent.write_json(request, {"action": "retry_quota_once", "event_id": eid})
        paused = self.cycle(lambda *_a, **_k: self.fail("Quota pause MUST block Codex"))
        paused.assert_not_called()
        agent.write_json(self.results / "quota-resume-request.json", {
            "action": "retry_after_user_confirmed_reset", "pause_id": pause["id"]})
        attempts = []
        def retry(p, settings, **kw):
            attempts.append((p, kw.get("retry_quota")))
            return {"status": "tests_passed_review_required",
                    "reason": "offline synthetic success, not actual Codex"}
        processed = self.cycle(retry)
        self.assertEqual(processed.call_count, 1)
        self.assertEqual(attempts, [(folder, True)])
        self.assertFalse(request.exists())
        self.assertNotIn("quota_pause", agent.read_json(self.results / "watch-state.json"))
        self.assertEqual(agent.read_json(self.results / eid / "result.json")["status"],
                         "blocked")
        no_repeat = self.cycle(lambda *_a, **_k: self.fail("No automatic replay"))
        no_repeat.assert_not_called()

    def test_quota_retry_rejects_unrelated_and_chained_failures(self):
        folder = self.add_error()
        eid = agent.event_id(folder)
        self.assertFalse(agent.quota_retry_eligible(folder, self.results, eid))
        directory = self.results / eid
        directory.mkdir()
        result = {"event_id": eid, "event": folder.name, "status": "blocked",
                  "reason": "codex_unverified: codex_quota_exhausted"}
        for change in ({"status": "tests_failed"}, {"reason": "general_timeout"},
                       {"retry_of": "a" * 16}):
            agent.write_json(directory / "result.json", {**result, **change})
            self.assertFalse(agent.quota_retry_eligible(folder, self.results, eid))
        agent.write_json(directory / "result.json", result)
        self.assertTrue(agent.quota_retry_eligible(folder, self.results, eid))
        other = self.results / agent.quota_retry_id(eid)
        other.mkdir()
        agent.write_json(other / "result.json", {"status": "blocked"})
        self.assertFalse(agent.quota_retry_eligible(folder, self.results, eid))

    def test_quota_job_id_is_distinct_and_stable(self):
        eid = "b" * 16
        new = agent.quota_retry_id(eid)
        self.assertRegex(new, r"^[0-9a-f]{16}$")
        self.assertNotEqual(new, eid)
        self.assertNotEqual(new, agent.interrupted_retry_id(eid))
        self.assertEqual(new, agent.quota_retry_id(eid))

    def test_readiness_is_safe_local_only(self):
        (self.repo / ".git").mkdir()
        for suite in ("altering", "crafting", "production-ui", "combined-scheduler"):
            d = self.repo / "tests" / suite
            d.mkdir(parents=True)
            (d / "Regression.csproj").write_text("<Project/>")
        calls = []
        def safe_cmd(argv, *args, **kwargs):
            calls.append(argv)
            return subprocess.CompletedProcess(argv, 0, "", "")
        with mock.patch.object(agent.shutil, "which", side_effect=lambda n: "/bin/" + n), \
             mock.patch.object(agent, "cmd", side_effect=safe_cmd), \
             mock.patch.object(github_pr, "user_opted_in", return_value=True):
            ok, reasons = agent.pipeline_readiness(self.config)
        self.assertTrue(ok, reasons)
        self.assertEqual(reasons, [])
        self.assertTrue(any(x[:3] == ["codex", "login", "status"] for x in calls))
        self.assertTrue(any(x[:3] == ["gh", "auth", "status"] for x in calls))
        self.assertFalse(any("exec" in x or "push" in x or "create" in x for x in calls))
        self.assertEqual(list(self.work.iterdir()), [])

    def test_readiness_reports_missing_dependencies_without_running_codex(self):
        with mock.patch.object(agent.shutil, "which", return_value=None), \
             mock.patch.object(agent, "cmd") as run, \
             mock.patch.object(github_pr, "user_opted_in", return_value=False):
            ok, reasons = agent.pipeline_readiness(self.config)
        self.assertFalse(ok)
        self.assertIn("codex_missing", reasons)
        self.assertIn("source_clone_missing", reasons)
        run.assert_not_called()

    def test_fake_evidence_to_codex_test_pr_pipeline(self):
        folder = self.add_error()
        source = "source/FishingAutomation/CraftingAutomation.cs"
        test = "tests/crafting/Program.cs"
        actions = []
        def git(argv, cwd=None, timeout=30, input_text=None, env=None):
            actions.append(argv)
            if argv[:3] == ["git", "worktree", "add"]:
                worktree = Path(argv[5])
                worktree.mkdir(parents=True)
                for relative in (source, test):
                    dest = worktree / relative
                    dest.parent.mkdir(parents=True, exist_ok=True)
                    dest.write_text("// offline candidate")
            if argv[:3] == ["git", "rev-parse", "HEAD"]:
                return subprocess.CompletedProcess(argv, 0, "c" * 40 + "\n", "")
            return subprocess.CompletedProcess(argv, 0, "", "")
        def tested(worktree, outdir, config, added_tests):
            self.assertIn(["dotnet", "run", "--project",
                           "tests/crafting/Regression.csproj", "-c", "Release"], added_tests)
            return True, [{"test": 1, "ok": True}, {"test": 2, "ok": True}]
        with mock.patch.object(agent, "cmd", side_effect=git), \
             mock.patch.object(agent, "exec_codex", return_value=(True, "mock codex only")), \
             mock.patch.object(agent, "edited_paths", return_value=([source, test], [])), \
             mock.patch.object(agent, "test_patch", side_effect=tested) as tests, \
             mock.patch.object(agent, "patch_fingerprint", return_value="synthetic_fingerprint"), \
             mock.patch.object(agent, "related_attempts", return_value=[]), \
             mock.patch.object(agent, "record_attempt"), \
             mock.patch.object(agent, "publish_verified_draft_pr",
                               return_value={"status": "draft_created",
                                             "url": "https://github.com/skwlrna/Mbi_auto_update/pull/123"}) as pr:
            outcome = agent.process(folder, self.config)
        self.assertEqual(outcome["status"], "tests_passed_review_required")
        self.assertEqual(outcome["pr"]["status"], "draft_created")
        self.assertEqual(outcome["regression_test"]["status"], "passed")
        self.assertTrue(tests.called and pr.called)
        self.assertTrue(any(a[:3] == ["git", "worktree", "add"] for a in actions))
        self.assertFalse(any("push" in a or "release" in a for a in actions))
        self.assertEqual(agent.read_json(self.results / agent.event_id(folder) / "result.json")["status"],
                         "tests_passed_review_required")

    def test_quota_ui_requires_separate_human_approval(self):
        ui = (BASE / "source" / "FishingAutomation" / "AiAutopilotDashboard.cs").read_text()
        host = (BASE / "source" / "FishingAutomation" / "AiAutopilotHost.cs").read_text()
        self.assertIn("RequestQuotaBlockedRetry()", ui)
        self.assertIn('"retry_quota_once"', ui)
        self.assertIn('MessageBoxButtons.YesNo, MessageBoxIcon.Warning', ui)
        self.assertIn('Field(activity.RootElement, "status") == "quota_paused"', ui)
        self.assertIn('File.Move(temp, request, false)', ui)
        self.assertIn('"--pipeline-check"', host)
        self.assertIn("ReadToEndAsync()", host)
        self.assertIn("child.Kill(entireProcessTree: true)", host)


if __name__ == "__main__":
    unittest.main()
