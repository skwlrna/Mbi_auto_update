"""Offline phase/notification contracts; no Codex, network, Telegram or game I/O."""
import contextlib
import importlib.util
import io
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "source" / "FishingAutomation"
SCRIPT = APP / "ai-autopilot" / "autopilot.py"
SPEC = importlib.util.spec_from_file_location("ai_stage_notification_test", SCRIPT)
assert SPEC and SPEC.loader
agent = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(agent)


class HaltPoll(Exception):
    pass


class AiStageTelegramContracts(unittest.TestCase):
    def run_watcher_with_fake_result(self, outcome: str):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            reports, runtime, results = root / "mcp", root / "runtime", root / "results"
            for folder in (reports, runtime, results):
                folder.mkdir()
            event = runtime / "runtime_20261016_200000_000_abcd1234"
            event.mkdir()
            (event / "error.txt").write_text("test-only evidence", encoding="utf-8")
            past = time.time() - 300
            os.utime(event / "error.txt", (past, past))
            os.utime(event, (past, past))
            conf = agent.config()
            conf.update({"reports_dir": str(reports),
                         "runtime_reports_dir": str(runtime),
                         "results_dir": str(results),
                         "settle_seconds": 1,
                         "process_old_events_on_watch_start": True})
            snapshots = []
            original_write = agent.write_json

            def spy_write(path, value):
                if Path(path).name == "watch-activity.json":
                    snapshots.append(dict(value))
                return original_write(path, value)

            def fake_process(_folder, _config, **options):
                for phase in ("evidence", "codex_edit", "diff_review", "regression", "draft"):
                    options["phase"](phase)
                    if phase == "codex_edit":
                        options["progress"]("running", 60, 2, "codex_running")
                return {"status": outcome, "reason": "offline_stub",
                        "event_id": agent.event_id(event)}

            output = io.StringIO()
            with mock.patch.object(agent, "require_isolated_ai_mode"), \
                 mock.patch.object(agent, "process", side_effect=fake_process), \
                 mock.patch.object(agent, "write_json", side_effect=spy_write), \
                 mock.patch.object(agent.time, "sleep", side_effect=HaltPoll), \
                 contextlib.redirect_stdout(output):
                with self.assertRaises(HaltPoll):
                    agent.poll(conf)
            return agent.event_id(event), output.getvalue(), snapshots

    def test_heartbeat_keeps_real_phase_and_start_timestamp(self):
        eid, stream, snapshots = self.run_watcher_with_fake_result("tests_passed_review_required")
        self.assertIn(f"AI 알림: started {eid}", stream)
        self.assertIn(f"AI 알림: result {eid} tests_passed_review_required", stream)
        self.assertIn("AI 단계: codex_edit", stream)
        self.assertIn("AI 단계: regression", stream)
        processing = [x for x in snapshots if x["status"] == "processing"]
        self.assertGreaterEqual(len(processing), 6)
        self.assertTrue(all(x["phase"] in
            {"evidence", "codex_edit", "diff_review", "regression", "repair", "draft"}
            for x in processing))
        codex = [x for x in processing if x["phase"] == "codex_edit"]
        self.assertGreaterEqual(len(codex), 2)
        self.assertEqual(codex[0]["phase_started"], codex[1]["phase_started"])
        self.assertEqual(processing[-1]["phase"], "draft")
        self.assertEqual(snapshots[-1]["status"], "watching")
        self.assertNotIn("automatic_merge_deploy", [x for x in snapshots if x.get("automatic_merge_deploy")])

    def test_blocked_result_alert_only_once(self):
        eid, stream, snapshots = self.run_watcher_with_fake_result("blocked")
        self.assertEqual(stream.count(f"AI 알림: started {eid}"), 1)
        self.assertEqual(stream.count(f"AI 알림: result {eid} blocked"), 1)
        self.assertTrue(all(x["automatic_merge_deploy"] is False for x in snapshots))

    def test_ui_uses_opt_in_text_only_alerts_and_fixed_categories(self):
        main = (APP / "MainForm.AiAutopilot.cs").read_text(encoding="utf-8")
        host = (APP / "AiAutopilotHost.cs").read_text(encoding="utf-8")
        notifier = (APP / "TelegramNotifier.cs").read_text(encoding="utf-8")
        settings = (APP / "TelegramSettingsDialog.cs").read_text(encoding="utf-8")
        board = (APP / "AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        agent_code = SCRIPT.read_text(encoding="utf-8")
        self.assertIn("AiAutopilotAlertsEnabled { get; set; } = false", notifier)
        self.assertIn("settings.AiAutopilotAlertsEnabled = dialog.AiAutopilotAlertsEnabled", settings)
        self.assertIn("settings.AiAutopilotAlertsEnabled", main)
        self.assertIn("screenshot: false", main)
        self.assertIn("NotificationRequested", main)
        self.assertIn("NotificationRequested", host)
        self.assertIn("id.Length != 16", host)
        self.assertIn("stage(\"codex_edit\")", agent_code)
        self.assertIn("stage(\"regression\")", agent_code)
        self.assertIn("phase_started", board)
        self.assertIn("DateTimeOffset.TryParse", board)
        self.assertNotIn("AI 알림: ", agent_code.split("중복 오류 건너뜀")[1].split("처리 시작:")[0])


if __name__ == "__main__":
    unittest.main()
