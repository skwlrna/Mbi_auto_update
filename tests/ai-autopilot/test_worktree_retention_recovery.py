"""Offline retention safety tests: no Codex, game, network, or deployment."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

AI = Path(__file__).resolve().parents[2] / "source/FishingAutomation/ai-autopilot"
sys.path.insert(0, str(AI))
import autopilot
import worktree_retention as retention
import pipeline_readiness


def git(repo: Path, *args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True,
                          capture_output=True, text=True).stdout.strip()


class WorktreeRetentionTest(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        git(self.repo, "init")
        git(self.repo, "config", "user.email", "test@example.invalid")
        git(self.repo, "config", "user.name", "Contract Test")
        (self.repo / "README").write_text("safe", encoding="utf-8")
        git(self.repo, "add", ".")
        git(self.repo, "commit", "-m", "base")
        self.workroot = self.root / "worktrees"
        self.workroot.mkdir()
        self.results = self.root / "results"
        self.results.mkdir()
        self.config = {"repo": str(self.repo), "results_dir": str(self.results),
                       "worktrees_dir": str(self.workroot), "max_worktrees": 1,
                       "worktree_archives_dir": str(self.root / "archives")}
        self.id = "ec6b620be2bd72a2"
        self.worktree = self.workroot / self.id
        git(self.repo, "worktree", "add", "-b", "ai/retention-test", str(self.worktree))
        report_dir = self.results / self.id
        report_dir.mkdir()
        self.original = report_dir / "result.json"
        self.original.write_text(json.dumps({
            "event": "runtime_example", "event_id": self.id, "status": "blocked",
            "reason": "other_failure", "worktree": str(self.worktree)
        }), encoding="utf-8")

    def test_read_only_inventory_warns_but_does_not_clean(self):
        before = self.original.read_bytes()
        snapshot = retention.report(self.config)
        self.assertEqual(snapshot["count"], 1)
        self.assertEqual(snapshot["limit"], 1)
        self.assertEqual(snapshot["clean_candidates"], [self.id])
        self.assertTrue(snapshot["warning"])
        self.assertTrue(self.worktree.exists())
        self.assertEqual(before, self.original.read_bytes())

    def test_opt_in_archive_verified_and_removes_only_clean_terminal_worktree(self):
        original = self.original.read_bytes()
        result = retention.archive_clean_one(self.config, self.id)
        self.assertEqual(result["status"], "archived", result)
        archive = self.root / "archives" / (self.id + ".zip")
        self.assertTrue(archive.is_file())
        self.assertFalse(self.worktree.exists())
        self.assertEqual(original, self.original.read_bytes())
        self.assertIn("ai/retention-test", git(self.repo, "branch", "--list"))
        self.assertIn("README", __import__("zipfile").ZipFile(archive).namelist())

    def test_modified_code_protected_even_on_terminal_result(self):
        (self.worktree / "README").write_text("candidate patch")
        before = self.original.read_bytes()
        snapshot = retention.report(self.config)
        self.assertEqual(snapshot["clean_candidates"], [])
        result = retention.archive_clean_one(self.config, self.id)
        self.assertEqual(result["status"], "blocked")
        self.assertTrue(self.worktree.exists())
        self.assertFalse((self.root / "archives" / (self.id + ".zip")).exists())
        self.assertEqual(before, self.original.read_bytes())

    def test_live_or_reviewed_patches_remain_protected(self):
        for label, changes in [
            ("in_progress", {}),
            ("tests_passed_review_required", {}),
            ("needs_review", {"changed_files": ["README"]}),
            ("blocked", {"pr": {"status": "created"}})
        ]:
            with self.subTest(label=label):
                data = json.loads(self.original.read_text())
                data.update(changes, status=label)
                self.original.write_text(json.dumps(data))
                self.assertEqual(retention.report(self.config)["clean_candidates"], [])
                self.assertTrue(self.worktree.exists())

    def test_bad_ids_rejected(self):
        self.assertEqual(retention.archive_clean_one(self.config, "../repo")["status"], "blocked")
        self.assertTrue(self.worktree.exists())


class RetentionReplayTest(unittest.TestCase):
    def test_wrong_original_block_reason_does_not_run_codex(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            evidence = root / "runtime_event"
            evidence.mkdir()
            (evidence / "error.txt").write_text("original")
            c = autopilot.config()
            c.update({"results_dir": str(root / "results"),
                      "repo": str(root), "worktrees_dir": str(root / "worktrees")})
            eid = autopilot.event_id(evidence)
            original = root / "results" / eid / "result.json"
            original.parent.mkdir(parents=True)
            original.write_text(json.dumps({"event": evidence.name, "event_id": eid,
                                             "status": "blocked", "reason": "wrong"}))
            with mock.patch.object(autopilot, "exec_codex") as codex:
                value = autopilot.process(evidence, c, retry_retention=True)
            self.assertEqual(value["reason"], "retention_original_not_reviewable")
            codex.assert_not_called()
            self.assertEqual(json.loads(original.read_text())["reason"], "wrong")

    def test_preflight_uses_verified_snapshot_without_mutation(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            old, current = root / "old", root / "snapshot"
            current.mkdir()
            (current / "source/FishingAutomation").mkdir(parents=True)
            (current / "source/FishingAutomation/FishingAutomation.csproj").write_text("test")
            for item in ("tests/altering", "tests/altering-screen-integration",
                         "tests/crafting", "tests/production-ui"):
                folder = current / item
                folder.mkdir(parents=True)
                (folder / "Regression.csproj").write_text("test")
            records = root / "results"
            records.mkdir()
            sha = "a" * 40
            (records / "source-alignment.json").write_text(json.dumps({
                "status": "verified", "expected_release_sha": sha,
                "selected_repo": str(current)}))
            with mock.patch.object(pipeline_readiness, "release_commit", return_value=sha), \
                 mock.patch.object(pipeline_readiness, "check_checkout",
                                   side_effect=lambda repo, expected: (Path(repo) == current, "verified")), \
                 mock.patch.object(pipeline_readiness.shutil, "which", return_value="/usr/bin/test"):
                result = pipeline_readiness.readiness({"repo": str(old),
                    "results_dir": str(records)}, locate=lambda exe: "/usr/bin/test")
            self.assertTrue(result["source_revision_verified"])
            self.assertTrue(result["stages"]["source"])
            self.assertEqual(result["source_revision_reason"], "verified_pinned_snapshot")
            self.assertFalse(old.exists())


if __name__ == "__main__":
    unittest.main()
