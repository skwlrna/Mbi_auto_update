"""Regression coverage for local recovery and the trusted test registry."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
import zipfile

HERE = Path(__file__).resolve().parents[2]
AI = HERE / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import retention_lifecycle as lifecycle
import regression_policy as policy


class GitSandbox(unittest.TestCase):
    def git(self, root, *args):
        result = subprocess.run(["git", "-C", str(root), *args],
                                capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return result.stdout.strip()

    def setup_repo(self, root, state="tests_failed"):
        repo = root / "repo"
        repo.mkdir()
        self.git(repo, "init", "-q")
        (repo / "src.txt").write_text("before", encoding="utf-8")
        self.git(repo, "add", "src.txt")
        self.git(repo, "-c", "user.email=test@example.invalid", "-c",
                 "user.name=Test", "commit", "-qm", "seed")
        eid = "a" * 16
        folder = root / "worktrees" / eid
        folder.parent.mkdir()
        self.git(repo, "worktree", "add", "-q", "--detach", str(folder), "HEAD")
        (folder / "src.txt").write_text("modified by Codex", encoding="utf-8")
        (folder / "untracked.bin").write_bytes(b"untracked-but-backed-up")
        results = root / "results" / eid
        results.mkdir(parents=True)
        event = "runtime_20261021_132452_590"
        (results / "codex-last-1.md").write_text("diagnostics remain", encoding="utf-8")
        (results / "result.json").write_text(json.dumps({
            "event_id": eid, "event": event, "worktree": str(folder),
            "status": state, "reason": "build_or_regression_test_failed_after_retries",
            "changed_files": ["src.txt"]
        }), encoding="utf-8")
        report = root / "reports" / event
        report.mkdir(parents=True)
        (report / "error.txt").write_text("original evidence remains",
                                          encoding="utf-8")
        config = {"repo": str(repo), "original_repo": str(repo),
                  "worktrees_dir": str(folder.parent),
                  "worktree_archives_dir": str(root / "archives"),
                  "results_dir": str(root / "results"),
                  "reports_dir": str(root / "reports")}
        return config, repo, folder, results, report


class RecoveryLifecycleTests(GitSandbox):
    def test_changed_patch_and_logs_archived_verified_before_removal(self):
        with tempfile.TemporaryDirectory() as temporary:
            config, repo, folder, results, report = self.setup_repo(Path(temporary))
            with mock.patch.object(lifecycle, "_registrations",
                                   return_value={folder.resolve(): repo.resolve()}):
                action = lifecycle.archive_finished_one(config, folder.name)
            self.assertEqual(action["status"], "archived", action)
            self.assertFalse(folder.exists())
            self.assertEqual((report / "error.txt").read_text(), "original evidence remains")
            self.assertTrue((results / "result.json").exists())
            archive = Path(action["archive"])
            with zipfile.ZipFile(archive) as zipped:
                self.assertEqual(zipped.read("worktree/src.txt"), b"modified by Codex")
                self.assertEqual(zipped.read("worktree/untracked.bin"),
                                 b"untracked-but-backed-up")
                self.assertEqual(zipped.read("results/codex-last-1.md"),
                                 b"diagnostics remain")
                self.assertIn("evidence/error.txt", zipped.namelist())

    def test_in_progress_never_automatically_retired(self):
        with tempfile.TemporaryDirectory() as temporary:
            config, repo, folder, _, _ = self.setup_repo(Path(temporary), "in_progress")
            with mock.patch.object(lifecycle, "_registrations",
                                   return_value={folder.resolve(): repo.resolve()}):
                blocked = lifecycle.archive_finished_one(config, folder.name)
                preview = lifecycle.reset_preview(config)
            self.assertEqual(blocked["reason"], "not_terminal")
            self.assertEqual(preview["recoverable"][0]["state"], "in_progress")
            self.assertTrue(folder.exists())

    def test_unknown_owner_is_vetoed_even_for_explicit_reset(self):
        with tempfile.TemporaryDirectory() as temporary:
            config, _, folder, _, _ = self.setup_repo(Path(temporary))
            with mock.patch.object(lifecycle, "_registrations", return_value={}):
                preview = lifecycle.reset_preview(config)
                action = lifecycle.archive_finished_one(config, folder.name,
                                                       allow_interrupted=True)
            self.assertEqual(preview["blocked"][0]["reason"],
                             "untrusted_or_unregistered")
            self.assertEqual(action["reason"], "untrusted_or_unregistered_owner")
            self.assertTrue(folder.exists())

    def test_existing_corrupted_backup_never_authorizes_removal(self):
        with tempfile.TemporaryDirectory() as temporary:
            config, repo, folder, _, _ = self.setup_repo(Path(temporary))
            archive_dir = Path(config["worktree_archives_dir"])
            archive_dir.mkdir()
            (archive_dir / (folder.name + ".recovery.zip")).write_bytes(b"bad")
            with mock.patch.object(lifecycle, "_registrations",
                                   return_value={folder.resolve(): repo.resolve()}):
                action = lifecycle.archive_finished_one(config, folder.name)
            self.assertEqual(action["reason"], "existing_archive_conflicts")
            self.assertTrue(folder.exists())

    def test_user_requested_batch_retires_stopped_interrupted_job(self):
        with tempfile.TemporaryDirectory() as temporary:
            config, repo, folder, results, _ = self.setup_repo(
                Path(temporary), "in_progress")
            with mock.patch.object(lifecycle, "_registrations",
                                   return_value={folder.resolve(): repo.resolve()}):
                report = lifecycle.reset_legacy_batch(config)
            self.assertEqual(report["archive_and_reclaimed"], 1, report)
            self.assertEqual(report["blocked"], [])
            self.assertFalse(folder.exists())
            self.assertTrue((results / "result.json").exists())
            self.assertFalse(report["evidence_deleted"])


class RegisteredRegressionTests(GitSandbox):
    def test_preexisting_committed_suite_recovers_registry_gap(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repo = root / "repo"
            repo.mkdir()
            self.git(repo, "init", "-q")
            suite = repo / "tests" / "trusted-existing"
            suite.mkdir(parents=True)
            (suite / "Program.cs").write_text("test", encoding="utf-8")
            (suite / "Regression.csproj").write_text("<Project />", encoding="utf-8")
            self.git(repo, "add", ".")
            self.git(repo, "-c", "user.email=test@example.invalid", "-c",
                     "user.name=Test", "commit", "-qm", "baseline")
            commands, reason = policy.commands_for([
                "source/FishingAutomation/Ordinary.cs",
                "tests/trusted-existing/Program.cs"], repo)
            self.assertIsNone(reason)
            self.assertIn("tests/trusted-existing/Regression.csproj", commands[0])

    def test_agent_created_test_suite_remains_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            repo = root / "repo"
            repo.mkdir()
            self.git(repo, "init", "-q")
            self.git(repo, "-c", "user.email=test@example.invalid", "-c",
                     "user.name=Test", "commit", "--allow-empty", "-qm", "baseline")
            suite = repo / "tests" / "brand-new"
            suite.mkdir(parents=True)
            (suite / "Program.cs").write_text("test", encoding="utf-8")
            (suite / "Regression.csproj").write_text("<Project />", encoding="utf-8")
            _, reason = policy.commands_for([
                "source/FishingAutomation/Ordinary.cs",
                "tests/brand-new/Program.cs"], repo)
            self.assertEqual(reason, "regression_test_suite_not_registered")


if __name__ == "__main__":
    unittest.main()
