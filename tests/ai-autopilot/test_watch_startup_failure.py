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

    def test_host_does_not_log_raw_stderr_or_run_auto_update(self):
        host = (AI.parent / "AiAutopilotHost.cs").read_text(encoding="utf-8")
        self.assertIn("WATCHER STARTUP FAILED|", host)
        self.assertIn("JSONDecodeError", host)
        self.assertIn("watch-startup-diagnostic.json", host)
        self.assertIn("AI 감시 종료 (코드 ", host)
        self.assertNotIn("ex.Message", host)
        self.assertNotIn("UpdateManager.CheckLatestAsync", host)


if __name__ == "__main__":
    unittest.main()
