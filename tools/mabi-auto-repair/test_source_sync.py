"""Source Git fast-forward safety for unattended future version updates."""
import subprocess
import tempfile
import unittest
from pathlib import Path
from unittest import mock

import watch

BEFORE = "a" * 40
AFTER = "b" * 40
ORIGIN = "https://github.com/skwlrna/Mbi_auto_update.git"


def completed(args, text, status=0):
    return subprocess.CompletedProcess(args, status, text, "")


class SourceSyncTests(unittest.TestCase):
    def exercise(self, *, branch="main", origin=ORIGIN, pull_ok=True,
                 new_head=AFTER):
        root = Path("mock-source")
        commands = []

        def fake_run(args, **kwargs):
            self.assertEqual(args[:3], ["git", "-C", str(root)])
            self.assertTrue(kwargs.get("capture_output"))
            self.assertTrue(kwargs.get("text"))
            self.assertEqual(kwargs.get("timeout"), 90)
            action = args[3:]
            commands.append(action)
            if action == ["branch", "--show-current"]:
                return completed(args, branch)
            if action == ["remote", "get-url", "origin"]:
                return completed(args, origin)
            if action == ["pull", "--ff-only", "origin", "main"]:
                return completed(args, "", 0 if pull_ok else 1)
            raise AssertionError(action)

        with mock.patch.object(watch.repair, "clean_git_repo",
                               side_effect=[BEFORE, new_head]) as clean, \
             mock.patch.object(watch.subprocess, "run", side_effect=fake_run):
            try:
                result = watch.sync_source_repo(root)
                failure = None
            except ValueError as exc:
                result, failure = None, str(exc)
        return result, failure, commands, clean.call_count

    def test_valid_fast_forward_reloads_on_next_tick(self):
        result, error, commands, clean_count = self.exercise()
        self.assertTrue(result)
        self.assertIsNone(error)
        self.assertEqual(clean_count, 2)
        self.assertEqual(commands[-1], ["pull", "--ff-only", "origin", "main"])

    def test_already_current_keeps_regular_processing(self):
        result, error, commands, clean_count = self.exercise(new_head=BEFORE)
        self.assertFalse(result)
        self.assertIsNone(error)
        self.assertEqual(clean_count, 2)

    def test_wrong_branch_never_pulls(self):
        result, error, commands, clean_count = self.exercise(branch="repair")
        self.assertIsNone(result)
        self.assertIn("main branch", error)
        self.assertEqual(clean_count, 1)
        self.assertNotIn(["pull", "--ff-only", "origin", "main"], commands)

    def test_unknown_origin_never_pulls(self):
        result, error, commands, clean_count = self.exercise(
            origin="https://github.com/unknown/example")
        self.assertIsNone(result)
        self.assertIn("expected origin", error)
        self.assertEqual(clean_count, 1)
        self.assertNotIn(["pull", "--ff-only", "origin", "main"], commands)

    def test_failed_pull_never_continues_to_paid_AI(self):
        result, error, commands, clean_count = self.exercise(pull_ok=False)
        self.assertIsNone(result)
        self.assertIn("fast-forward blocked", error)
        self.assertEqual(clean_count, 1)
        self.assertEqual(commands[-1], ["pull", "--ff-only", "origin", "main"])

    def test_dirty_source_never_calls_git_pull(self):
        with mock.patch.object(watch.repair, "clean_git_repo",
                               side_effect=ValueError("Git checkout must be clean")), \
             mock.patch.object(watch.subprocess, "run") as run:
            with self.assertRaisesRegex(ValueError, "checkout must be clean"):
                watch.sync_source_repo(Path("mock-source"))
            run.assert_not_called()


if __name__ == "__main__":
    unittest.main()
