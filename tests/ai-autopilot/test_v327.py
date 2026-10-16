"""V3.2.7 offline GitHub draft PR and read-only dashboard regression."""
from pathlib import Path
import importlib.util
import os
import sys
import subprocess
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import github_pr

SOURCE = ROOT / "source" / "FishingAutomation"
BRANCH = "ai/autopilot-" + "a" * 16
EVENT = "a" * 16
CHANGED = ["source/FishingAutomation/dungeon/AlteringScreen.cs",
           "tests/altering/Program.cs"]
TESTS = [{"test": 1, "ok": True}, {"test": 2, "ok": True}]


class DraftPrSafetyTests(unittest.TestCase):
    def test_opt_in_defaults_off(self):
        with mock.patch.dict(os.environ, {"LOCALAPPDATA": ""}):
            self.assertFalse(github_pr.user_opted_in())
        with mock.patch.object(github_pr, "user_opted_in", return_value=False), \
             mock.patch.object(github_pr, "run") as runner:
            result = github_pr.publish_verified_draft_pr(
                Path("."), BRANCH, EVENT, CHANGED, Path("."), TESTS)
            self.assertEqual(result["status"], "disabled")
            runner.assert_not_called()

    def test_pr_allowlist_blocks_untrusted_branch_or_tests(self):
        with mock.patch.object(github_pr, "user_opted_in", return_value=True), \
             mock.patch.object(github_pr, "run") as runner:
            a = github_pr.publish_verified_draft_pr(
                Path("."), "evil/main", EVENT, CHANGED, Path("."), TESTS)
            b = github_pr.publish_verified_draft_pr(
                Path("."), BRANCH, EVENT, CHANGED, Path("."), [])
            c = github_pr.publish_verified_draft_pr(
                Path("."), BRANCH, EVENT, ["config/secrets.env"], Path("."), TESTS)
            self.assertEqual(a["reason"], "untrusted_branch_or_event")
            self.assertEqual(b["reason"], "tests_not_verified")
            self.assertEqual(c["reason"], "changed_file_allowlist_failed")
            runner.assert_not_called()

    def test_remote_allowlist(self):
        self.assertTrue(github_pr.trusted_origin(
            "https://github.com/skwlrna/Mbi_auto_update.git\n"))
        for origin in ("https://example.com/repo", "file:///tmp/another",
                       "git@evil.com:skwlrna/Mbi_auto_update.git",
                       "https://github.com/other/Mbi_auto_update.git"):
            self.assertFalse(github_pr.trusted_origin(origin))

    def test_draft_pr_only_after_all_gates(self):
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            calls = []
            def fake_run(argv, cwd, timeout=60):
                calls.append(argv)
                if argv[-3:] == ["remote", "get-url", "origin"]:
                    stdout = "https://github.com/skwlrna/Mbi_auto_update.git\n"
                elif argv[-2:] == ["branch", "--show-current"]:
                    stdout = BRANCH + "\n"
                elif argv[:2] == ["git", "diff"] and "--name-only" in argv:
                    stdout = "\0".join(CHANGED) + "\0"
                elif argv[:2] == ["git", "diff"] and "--binary" in argv:
                    stdout = "diff --git a/source/... b/source/...\n"
                elif argv[-2:] == ["rev-parse", "HEAD^"]:
                    stdout = "a" * 40 + "\n"
                elif argv[-3:] == ["ls-remote", "origin", "refs/heads/main"]:
                    stdout = "a" * 40 + "\trefs/heads/main\n"
                elif argv[-2:] == ["rev-parse", "HEAD"]:
                    stdout = "b" * 40 + "\n"
                elif argv[:3] == ["gh", "pr", "create"]:
                    stdout = "https://github.com/skwlrna/Mbi_auto_update/pull/123\n"
                else:
                    stdout = ""
                return subprocess.CompletedProcess(argv, 0, stdout, "")
            with mock.patch.object(github_pr, "user_opted_in", return_value=True), \
                 mock.patch.object(github_pr.shutil, "which", return_value="gh"), \
                 mock.patch.object(github_pr, "run", side_effect=fake_run):
                result = github_pr.publish_verified_draft_pr(
                    directory, BRANCH, EVENT, CHANGED, directory, TESTS)
            self.assertEqual(result["status"], "draft_created")
            self.assertEqual(result["url"],
                             "https://github.com/skwlrna/Mbi_auto_update/pull/123")
            self.assertTrue(any("--draft" in x and "--repo" in x for x in calls))
            self.assertTrue(any("push" in x and any(token.endswith("refs/heads/" + BRANCH) for token in x) for x in calls))
            self.assertFalse(any("merge" in x or "release" in x for x in calls))

    def test_draft_refuses_mismatched_remote(self):
        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(github_pr, "user_opted_in", return_value=True), \
                 mock.patch.object(github_pr.shutil, "which", return_value="gh"), \
                 mock.patch.object(github_pr, "run",
                     return_value=subprocess.CompletedProcess(
                         [], 0, "https://github.com/attacker/project.git\n", "")) as run:
                response = github_pr.publish_verified_draft_pr(
                    Path(temp), BRANCH, EVENT, CHANGED, Path(temp), TESTS)
            self.assertEqual(response["reason"], "origin_repository_mismatch")
            self.assertEqual(run.call_count, 1)

    def test_status_and_dashboard_wiring(self):
        source = (SOURCE / "MainForm.AiAutopilot.cs").read_text(encoding="utf-8")
        view = (SOURCE / "AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        agent = (AI / "autopilot.py").read_text(encoding="utf-8")
        self.assertIn("AutoCreatePr", source)
        self.assertIn("GitHub PR 자동 생성 동의", source)
        self.assertIn("AiAutopilotDashboard(", source)
        self.assertIn('publish_verified_draft_pr(', agent)
        self.assertIn('if output["pr"]["status"] == "draft_created"', agent)
        self.assertIn("ReadBounded(", view)
        self.assertIn("watch-activity.json", view)
        self.assertIn("IsTrustedPrUrl", view)
        self.assertNotIn("gh pr create", view)


if __name__ == "__main__":
    unittest.main()
