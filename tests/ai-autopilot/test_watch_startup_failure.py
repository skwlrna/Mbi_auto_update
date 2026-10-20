"""V3.2.46: fail-closed AI watcher crash evidence without leaking raw data."""
import contextlib
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

AI = Path(__file__).resolve().parents[2] / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import autopilot


class WatchStartupFailureTests(unittest.TestCase):
    def test_corrupt_prior_watch_state_stays_unchanged_and_reports_type(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            result_dir = root / "results"
            result_dir.mkdir()
            state = result_dir / "watch-state.json"
            raw = b'{old invalid "secret": "ghp_sensitive_token_example"}'
            state.write_bytes(raw)
            config = {"results_dir": str(result_dir), "reports_dir": str(root / "MCP")}
            with mock.patch.object(autopilot, "WATCH_STAGE", "watch_loop"), mock.patch.object(
                    autopilot, "WATCH_DIAGNOSTIC_ROOT", result_dir):
                try:
                    autopilot.poll(config)
                except json.JSONDecodeError as exc:
                    with contextlib.redirect_stdout(io.StringIO()) as stdout:
                        report = autopilot.watcher_startup_failure(exc)
                else:
                    self.fail("Corrupt watcher state must not be silently reset")
            self.assertEqual(state.read_bytes(), raw)
            self.assertEqual(report["stage"], "watch_loop")
            self.assertEqual(report["reason"], "JSONDecodeError")
            self.assertIn("WATCHER STARTUP FAILED|watch_loop|JSONDecodeError", stdout.getvalue())
            saved = result_dir / "watch-startup-diagnostic.json"
            self.assertTrue(saved.is_file())
            content = saved.read_text(encoding="utf-8")
            self.assertNotIn("sensitive", content)
            self.assertNotIn(str(root), content)
            self.assertEqual(json.loads(content)["game_input"], False)

    def test_unknown_exception_and_untrusted_messages_are_never_copied(self):
        class SecretError(Exception):
            pass
        with tempfile.TemporaryDirectory() as temp:
            result_dir = Path(temp)
            with mock.patch.object(autopilot, "WATCH_STAGE", "external=secret"), mock.patch.object(
                    autopilot, "WATCH_DIAGNOSTIC_ROOT", result_dir):
                try:
                    raise SecretError("leak PII or token: ghp_secret")
                except SecretError as exc:
                    with contextlib.redirect_stdout(io.StringIO()) as stdout:
                        report = autopilot.watcher_startup_failure(exc)
            self.assertEqual(report["stage"], "initialization")
            self.assertEqual(report["reason"], "Exception")
            saved = (result_dir / "watch-startup-diagnostic.json").read_text(encoding="utf-8")
            self.assertNotIn("ghp_secret", saved)
            self.assertNotIn("ghp_secret", stdout.getvalue())
            self.assertNotIn("SecretError", saved)

    def test_stack_traces_contain_only_safe_repo_basename_and_line(self):
        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(autopilot, "WATCH_STAGE", "watch_loop"), mock.patch.object(
                    autopilot, "WATCH_DIAGNOSTIC_ROOT", Path(temp)):
                try:
                    # Exception from outside the AI package must never include
                    # sensitive file paths in the stored diagnostic.
                    raise TypeError("C:/Users/secret_user/credentials.json")
                except TypeError as exc:
                    with contextlib.redirect_stdout(io.StringIO()):
                        report = autopilot.watcher_startup_failure(exc)
            self.assertEqual(report["reason"], "TypeError")
            self.assertEqual(report["frames"], [])
            saved = (Path(temp) / "watch-startup-diagnostic.json").read_text("utf-8")
            self.assertNotIn("secret_user", saved)

    def test_lock_open_access_denied_is_distinct_and_file_is_preserved(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            lock_file = root / "autopilot.lock"
            marker = b"important-existing-lock"
            lock_file.write_bytes(marker)
            original = Path.open
            def deny_only_lock(path, *args, **kwargs):
                if path == lock_file:
                    raise PermissionError(13, "secret path and tokens")
                return original(path, *args, **kwargs)
            with mock.patch.object(Path, "open", new=deny_only_lock):
                with self.assertRaises(autopilot.WatcherLockFailure) as error:
                    with autopilot.SingletonLock(lock_file):
                        self.fail("Never run watcher if lock cannot open")
            self.assertEqual(error.exception.code, "lock_file_open_access_denied")
            self.assertEqual(lock_file.read_bytes(), marker)
            self.assertEqual(sorted(x.name for x in root.iterdir()), ["autopilot.lock"])

    def test_lock_file_windows_sharing_denial_does_not_claim_holder(self):
        error = PermissionError(13, "private sensitive file path")
        error.winerror = 32
        self.assertEqual(autopilot.lock_open_failure(error),
                         "lock_file_open_sharing_violation")
        self.assertEqual(autopilot.lock_open_failure(
            PermissionError(13, "private sensitive file path")),
            "lock_file_open_access_denied")

    def test_os_lock_contends_and_releases_without_force_or_delete(self):
        with tempfile.TemporaryDirectory() as temp:
            lock_file = Path(temp) / "autopilot.lock"
            with autopilot.SingletonLock(lock_file):
                self.assertTrue(lock_file.is_file())
                with self.assertRaises(autopilot.WatcherLockFailure) as error:
                    with autopilot.SingletonLock(lock_file):
                        self.fail("Second watcher must not start")
                self.assertEqual(error.exception.code, "lock_byte_held")
            # After the first watcher exits the OS must release its lock.
            with autopilot.SingletonLock(lock_file):
                self.assertTrue(lock_file.exists())

    def test_lock_diagnostic_only_allowlisted_reason_no_sensitive_paths(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp)
            with mock.patch.object(autopilot, "WATCH_DIAGNOSTIC_ROOT", target):
                with contextlib.redirect_stdout(io.StringIO()) as stdout:
                    autopilot.report_lock_failure(autopilot.WatcherLockFailure(
                        "secret=C:/Users/someone/token"))
            saved = (target / "watch-startup-diagnostic.json").read_text("utf-8")
            self.assertIn("lock_operation_failed", saved)
            self.assertNotIn("someone", saved)
            self.assertNotIn("secret=", stdout.getvalue())
            self.assertFalse(json.loads(saved)["codex_started"])

    def test_symlink_or_directory_lock_is_not_opened(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            folder = root / "autopilot.lock"
            folder.mkdir()
            with self.assertRaises(autopilot.WatcherLockFailure) as raised:
                with autopilot.SingletonLock(folder):
                    pass
            self.assertEqual(raised.exception.code, "lock_file_is_directory")
            folder.rmdir()
            external = root / "protected"
            external.write_text("never overwrite")
            try:
                folder.symlink_to(external)
            except (OSError, NotImplementedError):
                return
            with self.assertRaises(autopilot.WatcherLockFailure) as raised:
                with autopilot.SingletonLock(folder):
                    pass
            self.assertEqual(raised.exception.code, "lock_file_symlink")
            self.assertEqual(external.read_text(), "never overwrite")

    def test_host_does_not_log_raw_stderr_or_run_auto_update(self):
        host = (AI.parent / "AiAutopilotHost.cs").read_text(encoding="utf-8")
        self.assertIn("WATCHER STARTUP FAILED|", host)
        self.assertIn("WATCHER LOCK BLOCKED|", host)
        self.assertIn("lock_file_open_access_denied", host)
        self.assertIn("JSONDecodeError", host)
        self.assertIn("watch-startup-diagnostic.json", host)
        self.assertIn("AI 감시 종료 (코드 ", host)
        self.assertNotIn("ex.Message", host)
        self.assertNotIn("UpdateManager.CheckLatestAsync", host)


if __name__ == "__main__":
    unittest.main()
