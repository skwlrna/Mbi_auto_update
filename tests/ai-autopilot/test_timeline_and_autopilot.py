"""Regression contracts for local alert-to-Codex flow and readable AI timeline.

Read-only checks: no Codex, GitHub, game input, updater, or network requests.
"""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "source" / "FishingAutomation"
AUTOPILOT = SOURCE / "ai-autopilot" / "autopilot.py"
CONFIG = SOURCE / "ai-autopilot" / "config.json"
UPLOADER = SOURCE / "RuntimeErrorUploader.cs"
DASHBOARD = SOURCE / "AiAutopilotDashboard.cs"
MAIN = SOURCE / "MainForm.AiAutopilot.cs"


class TimelineAndAutopilotContracts(unittest.TestCase):
    def test_legacy_autofix_no_longer_blocks_codex_watcher(self):
        watcher = AUTOPILOT.read_text(encoding="utf-8")
        config = json.loads(CONFIG.read_text(encoding="utf-8"))
        self.assertNotIn("require_legacy_off", watcher)
        self.assertNotIn("legacy_autofix_guard", watcher)
        self.assertNotIn("require_autofix_off", config)
        self.assertIn("with SingletonLock(", watcher)
        self.assertIn("require_clean_repo", watcher)
        self.assertIn("regression_commands_for(", watcher)

    def test_runtime_alert_writes_local_report_before_optional_github(self):
        src = UPLOADER.read_text(encoding="utf-8")
        method = src[src.index("public async Task UploadAlertAsync("):]
        self.assertLess(method.index("SaveLocalAiEvidence(report);"),
                        method.index("if (!settings.Enabled) return;"))
        self.assertIn('Path.Combine(root, eventName)', src)
        self.assertIn('Path.Combine(folder, "error.txt")', src)
        self.assertIn('Path.Combine(folder, "summary.json")', src)
        self.assertIn('Path.Combine(folder, "trace.txt")', src)
        self.assertIn('"MabiAutoMCP", "reports"', src)

    def test_watcher_discovers_local_alert_as_regular_event(self):
        spec = importlib.util.spec_from_file_location("mabiauto_timeline_test", AUTOPILOT)
        self.assertIsNotNone(spec)
        import sys
        sys.path.insert(0, str(AUTOPILOT.parent))
        agent = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(agent)
        with tempfile.TemporaryDirectory() as tmp:
            folder = Path(tmp) / "error_20261016_051737_test"
            folder.mkdir()
            (folder / "error.txt").write_text(
                "오류 유형: 다중가공 오류\n오류 내용: 어비스 퇴장 HUD 확인 실패",
                encoding="utf-8")
            self.assertEqual([p.name for p in agent.evidence_folders(Path(tmp))],
                             [folder.name])
            prompt_data = agent.context_for(folder)
            self.assertIn("어비스 퇴장 HUD 확인 실패", prompt_data)
            self.assertNotIn("NO SCREENSHOT BYTES SHARED" + "XX", prompt_data)

    def test_dashboard_includes_local_timestamp_error_and_next_step(self):
        src = DASHBOARD.read_text(encoding="utf-8")
        for heading in ("발생 일시", "무슨 오류인가요?", "AI 처리 상태", "다음 단계"):
            self.assertIn(heading, src)
        self.assertIn('Field(summary.RootElement, "timeLocal", 90)', src)
        self.assertIn('Field(summary.RootElement, "detail", 450)', src)
        self.assertIn("timeline.OrderByDescending(x => x.When)", src)
        self.assertIn("ShowSelectedDetail();", src)
        self.assertIn('"AI 분석 기록 없음"', src)
        self.assertIn("실게임", src)

    def test_macro_close_still_kills_only_its_child_watcher(self):
        host = (SOURCE / "AiAutopilotHost.cs").read_text(encoding="utf-8")
        main = MAIN.read_text(encoding="utf-8")
        self.assertIn("child.Kill(entireProcessTree: true)", host)
        self.assertIn("_aiHost?.Dispose();", main)
        self.assertIn("AI 감시와 진행 중인 작업 함께 중지", main)


if __name__ == "__main__":
    unittest.main()
