"""Offline regressions for AI cleanup / UI contracts, no Codex or live game."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

HERE = Path(__file__).resolve().parents[2]
AI = HERE / "source/FishingAutomation/ai-autopilot"
sys.path.insert(0, str(AI))
import autopilot
import worktree_retention


class StopPoll(Exception):
    pass


def git(repo, *args):
    return subprocess.run(["git", "-C", str(repo), *args], check=True,
                          capture_output=True, text=True).stdout.strip()


class AutomaticWorkspaceCleanupTest(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        git(self.repo, "init")
        git(self.repo, "config", "user.email", "offline@example.invalid")
        git(self.repo, "config", "user.name", "Offline Regression")
        git(self.repo, "remote", "add", "origin", "https://github.com/skwlrna/Mbi_auto_update.git")
        (self.repo / "README").write_text("original", encoding="utf8")
        git(self.repo, "add", ".")
        git(self.repo, "commit", "-m", "base")
        self.id = "a1234567890bcdef"
        self.worktrees = self.root / "worktrees"
        self.worktrees.mkdir()
        self.worktree = self.worktrees / self.id
        git(self.repo, "worktree", "add", "-b", "ai/offline-fixture", str(self.worktree))
        self.results = self.root / "results"
        self.results.mkdir()
        self.report = self.results / self.id / "result.json"
        self.report.parent.mkdir()
        self.report.write_text(json.dumps({
            "event": "runtime_offline", "event_id": self.id,
            "status": "blocked", "reason": "other_failure", "worktree": str(self.worktree)
        }), encoding="utf8")
        self.config = autopilot.config()
        self.config.update({
            "repo": str(self.repo), "require_clean_repo": True,
            "results_dir": str(self.results), "reports_dir": str(self.root / "mcp"),
            "runtime_reports_dir": str(self.root / "runtime"),
            "worktrees_dir": str(self.worktrees),
            "worktree_archives_dir": str(self.root / "archives"),
            "max_worktrees": 1, "settle_seconds": 1, "poll_seconds": 10
        })
        (self.root / "mcp").mkdir()
        (self.root / "runtime").mkdir()

    def run_one_cycle(self):
        with mock.patch.object(autopilot.time, "sleep", side_effect=StopPoll):
            with self.assertRaises(StopPoll):
                autopilot.poll(self.config)

    def test_near_limit_automatically_archives_only_clean_terminal_worktree(self):
        saved = self.report.read_bytes()
        self.run_one_cycle()
        self.assertFalse(self.worktree.exists())
        backup = self.root / "archives" / (self.id + ".zip")
        self.assertTrue(backup.is_file())
        self.assertEqual(self.report.read_bytes(), saved)
        self.assertIn("ai/offline-fixture", git(self.repo, "branch", "--list"))

    def test_changed_code_is_never_auto_deleted(self):
        (self.worktree / "README").write_text("valuable proposed patch", encoding="utf8")
        original = (self.worktree / "README").read_bytes()
        self.run_one_cycle()
        self.assertEqual((self.worktree / "README").read_bytes(), original)
        self.assertFalse((self.root / "archives" / (self.id + ".zip")).exists())

    def test_batch_user_request_with_revalidation(self):
        self.config["max_worktrees"] = 30
        request = self.results / "retention-batch-request.json"
        request.write_text(json.dumps({
            "action": "archive_clean_verified_batch_once", "event_ids": [self.id]
        }), encoding="utf8")
        self.run_one_cycle()
        self.assertFalse(request.exists())
        self.assertFalse(self.worktree.exists())
        self.assertTrue((self.root / "archives" / (self.id + ".zip")).is_file())

    def test_batch_never_removes_dirty_worktree(self):
        self.config["max_worktrees"] = 30
        (self.worktree / "README").write_text("pending code edit")
        request = self.results / "retention-batch-request.json"
        request.write_text(json.dumps({
            "action": "archive_clean_verified_batch_once", "event_ids": [self.id]
        }), encoding="utf8")
        self.run_one_cycle()
        self.assertFalse(request.exists())
        self.assertTrue(self.worktree.exists())
        self.assertFalse((self.root / "archives" / (self.id + ".zip")).exists())

    def test_capacity_blocked_event_gets_one_isolated_retry(self):
        # Remove an available clean terminal worktree first, then retry the
        # previously blocked error. No Codex can be launched in this fixture.
        self.report.write_text(json.dumps({
            "event": "runtime_offline", "event_id": self.id,
            "status": "blocked", "reason": "other_failure", "worktree": str(self.worktree)
        }))
        event = self.root / "runtime" / "runtime_20261020_090000_000_offline"
        event.mkdir()
        (event / "error.txt").write_text("offline error")
        old = time.time() - 300
        os.utime(event, (old, old))
        os.utime(event / "error.txt", (old, old))
        eid = autopilot.event_id(event)
        blocked = self.results / eid / "result.json"
        blocked.parent.mkdir()
        blocked.write_text(json.dumps({
            "event": event.name, "event_id": eid, "status": "blocked",
            "reason": "worktree_retention_limit_reached"
        }))
        (self.results / "watch-state.json").write_text(json.dumps({
            "seen": [eid], "processed": []
        }))
        old_bytes = blocked.read_bytes()
        calls = []
        def fake_process(folder, cfg, **kwargs):
            self.assertTrue(kwargs["retry_retention"])
            self.assertTrue(folder.samefile(event), 'same runtime event despite Windows short paths')
            calls.append(folder)
            # Model a result creation failure: the user must never be charged
            # for a second unapproved Codex attempt on the next watch cycle.
            return {"status": "blocked", "reason": "offline_fixture_no_result_file"}
        with mock.patch.object(autopilot, "process", side_effect=fake_process):
            self.run_one_cycle()
            self.run_one_cycle()
        self.assertEqual(len(calls), 1)
        self.assertEqual(blocked.read_bytes(), old_bytes)
        self.assertFalse(self.worktree.exists())
        state = json.loads((self.results / "watch-state.json").read_text())
        self.assertIn(eid, state["auto_retention_retry_attempted"])
        self.assertFalse((self.results / autopilot.retention_retry_id(eid) / "result.json").exists())


class DashboardContractsTest(unittest.TestCase):
    def test_deletion_ui_requires_approval_and_protects_worktrees(self):
        source = (HERE / "source/FishingAutomation/AiAutopilotDashboard.Cleanup.cs").read_text(
            encoding="utf8")
        self.assertIn("DeleteLocalHistory(bool all)", source)
        self.assertIn("_isWatcherRunning()", source)
        self.assertIn("MessageBoxButtons.YesNo", source)
        self.assertIn("HasRepairWorktree", source)
        self.assertIn("CanRemoveLocalFolder", source)
        self.assertIn("FileAttributes.ReparsePoint", source)
        self.assertNotIn("git worktree remove --force", source)

    def test_simple_ui_keeps_extra_actions_behind_advanced_control(self):
        source = (HERE / "source/FishingAutomation/AiAutopilotDashboard.cs").read_text(
            encoding="utf8")
        self.assertIn('Text = "선택 삭제"', source)
        self.assertIn('Text = "기록 전체 삭제"', source)
        self.assertIn('Text = "고급 ▾"', source)
        self.assertIn("MultiSelect = true", source)
        self.assertIn('Text = "작업공간 정리"', source)


if __name__ == "__main__":
    unittest.main()
