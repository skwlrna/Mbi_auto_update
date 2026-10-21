"""Offline Windows-compatible recovery/registry tests; never touch live MabiAutoAI."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
from worktree_recovery import eligibility, preview, recover_one
from regression_policy import commands_for

spec = importlib.util.spec_from_file_location("reclaim_test_agent", AI / "autopilot.py")
assert spec and spec.loader
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class GitRecoveryFixture(unittest.TestCase):
    event_id = "17150c5309a25c92"

    def git(self, folder: Path, *args: str) -> str:
        run = subprocess.run(["git", "-C", str(folder), *args],
                             text=True, capture_output=True, timeout=30)
        self.assertEqual(run.returncode, 0, run.stderr)
        return run.stdout.strip()

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.home = Path(self.tmp.name)
        self.repo = self.home / "repo"
        self.repo.mkdir()
        self.git(self.repo, "init")
        self.git(self.repo, "config", "user.email", "tests@example.invalid")
        self.git(self.repo, "config", "user.name", "Offline Test")
        self.git(self.repo, "remote", "add", "origin",
                 "https://github.com/skwlrna/Mbi_auto_update.git")
        (self.repo / "src.txt").write_text("original\n", encoding="utf-8")
        (self.repo / "tests" / "gathering").mkdir(parents=True)
        (self.repo / "tests" / "gathering" / "Program.cs").write_text(
            "test\n", encoding="utf-8")
        (self.repo / "tests" / "gathering" / "Regression.csproj").write_text(
            "<Project />\n", encoding="utf-8")
        (self.repo / "tests" / "new-suite").mkdir(parents=True)
        (self.repo / "tests" / "new-suite" / "Program.cs").write_text(
            "test\n", encoding="utf-8")
        (self.repo / "tests" / "new-suite" / "Regression.csproj").write_text(
            "<Project />\n", encoding="utf-8")
        (self.repo / "tests" / "ai-autopilot").mkdir(parents=True)
        (self.repo / "tests" / "ai-autopilot" / "test_autofix.py").write_text(
            "import unittest\n", encoding="utf-8")
        self.git(self.repo, "add", ".")
        self.git(self.repo, "commit", "-m", "baseline")
        self.worktrees = self.home / "worktrees"
        self.worktrees.mkdir()
        self.workspace = self.worktrees / self.event_id
        self.git(self.repo, "worktree", "add", "-b", "ai/test-recovery", str(self.workspace))
        self.results = self.home / "results"
        (self.results / self.event_id).mkdir(parents=True)
        self.reports = self.home / "reports"
        (self.reports / "incident-one").mkdir(parents=True)
        (self.reports / "incident-one" / "error.txt").write_text(
            "original evidence\n", encoding="utf-8")
        self.config = {
            "repo": str(self.repo),
            "original_repo": str(self.repo),
            "worktrees_dir": str(self.worktrees),
            "results_dir": str(self.results),
            "reports_dir": str(self.reports),
            "worktree_archives_dir": str(self.home / "archives"),
            "max_worktrees": 30,
        }

    def result(self, status: str):
        record = {
            "event_id": self.event_id, "event": "incident-one",
            "worktree": str(self.workspace), "status": status,
            "changed_files": ["src.txt"],
            "reason": "build_or_regression_test_failed_after_retries",
        }
        (self.results / self.event_id / "result.json").write_text(
            json.dumps(record), encoding="utf-8")

    def test_modified_failed_worktree_archives_entire_diff_and_logs_before_removal(self):
        self.result("tests_failed")
        (self.workspace / "src.txt").write_text("Codex patch\n", encoding="utf-8")
        (self.results / self.event_id / "test-1.log").write_text(
            "failed assertion\n", encoding="utf-8")
        snapshot = preview(self.config)
        self.assertEqual(snapshot["count"], 1)
        self.assertEqual(len(snapshot["recoverable"]), 1)
        outcome = recover_one(self.config, self.event_id)
        self.assertEqual(outcome["status"], "archived", outcome)
        self.assertFalse(self.workspace.exists())
        self.assertTrue((self.results / self.event_id / "test-1.log").exists())
        self.assertTrue((self.reports / "incident-one" / "error.txt").exists())
        with zipfile.ZipFile(outcome["archive"]) as z:
            self.assertEqual(z.read("worktree/src.txt"), b"Codex patch\n")
            self.assertEqual(z.read("result/test-1.log"), b"failed assertion\n")
            self.assertEqual(z.read("evidence_reports_dir/error.txt"), b"original evidence\n")
            manifest = json.loads(z.read("RECOVERY-MANIFEST.json"))
            self.assertEqual(manifest["event_id"], self.event_id)
            self.assertTrue(manifest["original_logs_preserved"])

    def test_still_running_is_protected_even_in_legacy_mode(self):
        self.result("in_progress")
        self.assertEqual(eligibility(self.config, self.event_id, legacy=True)[0],
                         "protected")
        self.assertEqual(recover_one(self.config, self.event_id,
                                     legacy=True)["status"], "blocked")
        self.assertTrue(self.workspace.exists())

    def test_legacy_missing_result_requires_explicit_option_and_archives(self):
        self.assertEqual(preview(self.config)["recoverable"], [])
        self.assertEqual(len(preview(self.config, legacy=True)["recoverable"]), 1)
        result = recover_one(self.config, self.event_id, legacy=True)
        self.assertEqual(result["status"], "archived", result)

    def test_unknown_untracked_content_is_archived_not_silently_removed(self):
        self.result("blocked")
        (self.workspace / "unknown_user_file.txt").write_text("keep me")
        result = recover_one(self.config, self.event_id)
        self.assertEqual(result["status"], "archived", result)
        with zipfile.ZipFile(result["archive"]) as z:
            self.assertEqual(z.read("worktree/unknown_user_file.txt"), b"keep me")

    def test_existing_unlisted_baseline_suite_is_accepted_but_new_suite_is_not(self):
        changed = ["source/FishingAutomation/MainForm.Crafting.cs",
                   "tests/new-suite/Program.cs"]
        commands, error = commands_for(changed, self.workspace)
        self.assertIsNone(error)
        self.assertTrue(commands and commands[0][0] == "dotnet")
        (self.workspace / "tests" / "invented").mkdir()
        (self.workspace / "tests" / "invented" / "Program.cs").write_text("test")
        (self.workspace / "tests" / "invented" / "Regression.csproj").write_text(
            "<Project />")
        commands, error = commands_for([
            "source/FishingAutomation/MainForm.Crafting.cs",
            "tests/invented/Program.cs"], self.workspace)
        self.assertEqual(error, "regression_test_suite_not_registered")

    def test_ai_python_source_requires_existing_python_test(self):
        python_test = "tests/ai-autopilot/test_autofix.py"
        commands, err = commands_for([
            "source/FishingAutomation/ai-autopilot/autopilot.py",
            python_test], self.workspace)
        self.assertIsNone(err)
        self.assertTrue(any("-m" in args and "unittest" in args
                            for args in commands))
        _, err = commands_for([
            "source/FishingAutomation/ai-autopilot/autopilot.py",
            "tests/gathering/Program.cs"], self.workspace)
        self.assertEqual(err, "python_regression_test_missing")

    def test_cause_classification_is_not_changed_files_equals_success(self):
        self.assertEqual(agent.repair_outcome_category(
            "needs_review", "regression_test_suite_not_registered"),
            "regression_policy_block")
        self.assertEqual(agent.repair_outcome_category(
            "needs_review", "agent_no_patch"), "code_unchanged")
        self.assertEqual(agent.repair_outcome_category(
            "tests_failed", "build_or_regression_test_failed_after_retries",
            [{"tests": [{"ok": False, "reason": "exit_1"}]}]),
            "actual_test_failure")
        self.assertEqual(agent.repair_outcome_category(
            "blocked", "codex_unverified:cli_exit_2"),
            "environment_or_codex_execution_error")


if __name__ == "__main__":
    unittest.main()
