"""V3.2.19 candidate: safe post-quota replay and genuine-pipeline boundary checks.

GitHub CI uses mocks for Codex/tests/GitHub publication; it MUST NOT be reported
as a real-PC Codex or live-game success. No remote push or game input.
"""
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
AIDIR = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AIDIR))
import pipeline_readiness

spec = importlib.util.spec_from_file_location("v2_ai_under_test", AIDIR / "autopilot.py")
assert spec and spec.loader
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class EndCycle(Exception):
    pass


class QuotaManualReplay(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.reports = root / "reports"
        self.results = root / "results"
        self.reports.mkdir()
        self.results.mkdir()
        self.folder = self.reports / "runtime_20261016_100000_000_f1edbeef"
        self.folder.mkdir()
        err = self.folder / "error.txt"
        err.write_text("safe representative application error", encoding="utf-8")
        old = time.time() - 90
        os.utime(err, (old, old))
        os.utime(self.folder, (old, old))
        self.eid = agent.event_id(self.folder)
        (self.results / self.eid).mkdir()
        agent.write_json(self.results / self.eid / "result.json", {
            "event": self.folder.name, "event_id": self.eid, "status": "blocked",
            "reason": "codex_unverified: codex_quota_exhausted",
            "worktree": "original_isolated_patch_must_be_preserved"})
        agent.write_json(self.results / "watch-state.json",
                         {"seen": [self.eid], "processed": [{"id": self.eid, "status": "blocked"}]})
        self.c = agent.config()
        self.c.update({"reports_dir": str(self.reports),
                       "runtime_reports_dir": str(root / "empty_runtime"),
                       "results_dir": str(self.results),
                       "settle_seconds": 1, "poll_seconds": 10})

    def cycle(self, on_process):
        with mock.patch.object(agent, "require_isolated_ai_mode"), \
             mock.patch.object(agent, "process", side_effect=on_process) as mocked, \
             mock.patch.object(agent.time, "sleep", side_effect=EndCycle):
            with self.assertRaises(EndCycle):
                agent.poll(self.c)
        return mocked

    def request(self, event_id=None):
        agent.write_json(self.results / "quota-retry-request.json",
                         {"action": "retry_quota_blocked_once",
                          "event_id": event_id or self.eid})

    def test_no_request_means_no_automatic_quota_retry(self):
        self.cycle(lambda *args, **kw: self.fail("old error must never rerun"))
        self.assertEqual(agent.read_json(self.results / self.eid / "result.json")["status"], "blocked")

    def test_pending_request_never_bypasses_provider_pause(self):
        self.request()
        state = agent.read_json(self.results / "watch-state.json")
        state["quota_pause"] = agent.pause_record()
        agent.write_json(self.results / "watch-state.json", state)
        called = self.cycle(lambda *args, **kw: self.fail("paused Codex must not run"))
        called.assert_not_called()
        self.assertTrue((self.results / "quota-retry-request.json").exists())

    def test_one_approved_retry_only_after_matching_user_resume(self):
        self.request()
        state = agent.read_json(self.results / "watch-state.json")
        state["quota_pause"] = agent.pause_record()
        agent.write_json(self.results / "watch-state.json", state)
        self.cycle(lambda *args, **kw: self.fail("no call before provider reset"))
        agent.write_json(self.results / "quota-resume-request.json",
                         {"action": "retry_after_user_confirmed_reset",
                          "pause_id": state["quota_pause"]["id"]})
        def verify(p, config, **kwargs):
            self.assertEqual(p, self.folder)
            self.assertTrue(kwargs.get("retry_quota"))
            self.assertFalse(kwargs.get("retry_interrupted"))
            return {"status": "needs_review", "reason": "offline_verification_only"}
        calls = self.cycle(verify)
        self.assertEqual(calls.call_count, 1)
        self.assertFalse((self.results / "quota-retry-request.json").exists())
        self.assertNotIn("quota_pause", agent.read_json(self.results / "watch-state.json"))
        old = agent.read_json(self.results / self.eid / "result.json")
        self.assertEqual(old["status"], "blocked")
        self.assertEqual(old["worktree"], "original_isolated_patch_must_be_preserved")
        repeat = self.cycle(lambda *args, **kw: self.fail("retry consumed already"))
        repeat.assert_not_called()

    def test_invalid_or_unrelated_original_never_retries(self):
        self.request(event_id="../not_an_id")
        self.cycle(lambda *args, **kw: self.fail("invalid ID"))
        self.assertEqual(agent.quota_retry_eligible(
            {"event": self.folder.name, "event_id": self.eid,
             "status": "blocked", "reason": "unrelated_error"},
            self.eid, self.folder.name), False)
        self.assertFalse(agent.quota_retry_eligible(
            {"event": "unrelated", "event_id": self.eid,
             "status": "blocked", "reason": "codex_quota_exhausted"},
            self.eid, self.folder.name))

    def test_replay_gets_distinct_bounded_identifier(self):
        nxt = agent.quota_retry_id(self.eid)
        self.assertNotEqual(nxt, self.eid)
        self.assertEqual(len(nxt), 16)
        self.assertEqual(agent.quota_retry_id(self.eid), nxt)
        self.assertRegex(nxt, "^[0-9a-f]{16}$")


class ReadOnlyPipeline(unittest.TestCase):
    def test_readiness_exposes_unknown_real_outcomes(self):
        with tempfile.TemporaryDirectory() as temp:
            repo = Path(temp)
            for path in (
                "source/FishingAutomation/FishingAutomation.csproj",
                "tests/altering/Regression.csproj",
                "tests/altering-screen-integration/Regression.csproj",
                "tests/crafting/Regression.csproj",
                "tests/production-ui/Regression.csproj",
            ):
                f = repo / path
                f.parent.mkdir(parents=True, exist_ok=True)
                f.write_text("readiness only", encoding="utf-8")
            def fake_exec(cmd, **_kwargs):
                return subprocess.CompletedProcess(cmd, 0,
                    str(repo) + "\n" if "--show-toplevel" in cmd else "", "")
            with mock.patch.object(pipeline_readiness, "release_commit", return_value="a" * 40), \
                 mock.patch.object(pipeline_readiness, "check_checkout", return_value=(True, "release_source_verified")):
                result = pipeline_readiness.readiness(
                    {"repo": str(repo)}, opted_in=True,
                    locate=lambda x: "stub/" + x, execute=fake_exec)
            self.assertEqual(result["result"], "ready_for_manual_trial")
            self.assertTrue(all(result["stages"].values()))
            for forbidden in ("codex_executed", "patch_created", "tests_executed",
                              "draft_pr_created", "game_tested", "automatic_merge_deploy"):
                self.assertFalse(result[forbidden])
            no_git = pipeline_readiness.readiness(
                {"repo": str(repo)}, locate=lambda x: None, execute=fake_exec)
            self.assertEqual(no_git["result"], "needs_setup")
            self.assertFalse(no_git["stages"]["draft_pr"])

    def test_local_cli_mode_never_starts_codex(self):
        with tempfile.TemporaryDirectory() as temp:
            c = agent.config()
            c["results_dir"] = temp
            expected = {"kind": "local_readiness_only", "result": "needs_setup",
                        "stages": {"evidence": True, "source": False,
                                   "codex": False, "regressions": False,
                                   "draft_pr": False}}
            with mock.patch.object(agent.sys, "argv", ["autopilot.py", "--pipeline-check"]), \
                 mock.patch.object(agent, "config", return_value=c), \
                 mock.patch.object(agent, "readiness", return_value=expected), \
                 mock.patch.object(agent, "user_opted_in", return_value=False), \
                 mock.patch.object(agent, "poll") as polling, \
                 mock.patch.object(agent, "exec_codex") as codex:
                self.assertEqual(agent.main(), 2)
            polling.assert_not_called()
            codex.assert_not_called()
            self.assertFalse(agent.read_json(Path(temp) / "pipeline-check.json")["stages"]["source"])


@unittest.skipUnless(shutil.which("git"), "local git is required for isolated orchestration check")
class SimulatedPipeline(unittest.TestCase):
    def test_one_fake_codex_patch_through_real_git_isolation_and_mock_review_pr(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            repo = root / "repo"
            repo.mkdir()
            def git(*args):
                subprocess.run(["git", *args], cwd=repo,
                               check=True, capture_output=True, text=True, timeout=30)
            git("init")
            git("config", "user.name", "Offline Test")
            git("config", "user.email", "test@example.invalid")
            first = repo / "source/FishingAutomation/dungeon/AlteringScreen.cs"
            testfile = repo / "tests/altering-screen-integration/Program.cs"
            project = repo / "tests/altering-screen-integration/Regression.csproj"
            for p, data in ((first, "// original source\n"),
                            (testfile, "// original regression\n"),
                            (project, "<Project/>")):
                p.parent.mkdir(parents=True, exist_ok=True)
                p.write_text(data, encoding="utf-8")
            git("add", ".")
            git("commit", "-m", "offline baseline")
            folder = root / "reports" / "runtime_20261016_100100_000_c0ffee00"
            folder.mkdir(parents=True)
            (folder / "error.txt").write_text("screen state remained unchanged", encoding="utf-8")
            c = agent.config()
            c.update({"repo": str(repo), "results_dir": str(root / "results"),
                      "worktrees_dir": str(root / "trees"),
                      "reports_dir": str(folder.parent),
                      "test_commands": [["dotnet", "--version"]]})
            verified = []
            def fake_codex(worktree, prompt, outdir, settings, progress=None):
                for rel in ("source/FishingAutomation/dungeon/AlteringScreen.cs",
                            "tests/altering-screen-integration/Program.cs"):
                    file = worktree / rel
                    file.write_text(file.read_text(encoding="utf-8") + "// regression fix\n",
                                    encoding="utf-8")
                return True, "mock_only"
            def fake_tests(worktree, outdir, settings, extra):
                self.assertEqual(extra, [
                    ["dotnet", "run", "--project",
                     "tests/altering-screen-integration/Regression.csproj", "-c", "Release"]])
                verified.append(True)
                return True, [{"test": 1, "ok": True, "reason": "simulated_only"}]
            def fake_pr(worktree, branch, eid, changed, outdir, tests):
                self.assertTrue(verified)
                self.assertEqual(len(changed), 2)
                self.assertTrue(all(t["ok"] for t in tests))
                return {"status": "draft_created",
                        "url": "https://github.com/skwlrna/Mbi_auto_update/pull/123"}
            c["release_source_sha"] = subprocess.run(
                ["git", "rev-parse", "HEAD"], cwd=repo,
                capture_output=True, text=True, check=True).stdout.strip()
            with mock.patch.object(agent, "require_pinned_source", return_value=c["release_source_sha"]), \
                 mock.patch.object(agent, "prepare_dotnet_environment", return_value=(True, "offline_simulated")), \
                 mock.patch.object(agent, "exec_codex", side_effect=fake_codex), \
                 mock.patch.object(agent, "test_patch", side_effect=fake_tests), \
                 mock.patch.object(agent, "publish_verified_draft_pr", side_effect=fake_pr):
                result = agent.process(folder, c)
            self.assertEqual(result["status"], "tests_passed_review_required")
            self.assertEqual(result["pr"]["status"], "draft_created")
            self.assertTrue(Path(result["worktree"]).is_dir())
            self.assertTrue((root / "results" / result["event_id"] / "result.json").is_file())
            self.assertFalse((root / "results" / result["event_id"] / "user-feedback.json").exists())


if __name__ == "__main__":
    unittest.main()
