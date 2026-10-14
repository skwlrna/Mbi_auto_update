"""Static wiring checks for V3.2.5 AI status -> ordinary MabiAuto log."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
MAIN = ROOT / "source" / "FishingAutomation" / "MainForm.AiAutopilot.cs"
HOST = ROOT / "source" / "FishingAutomation" / "AiAutopilotHost.cs"
SCREEN = ROOT / "source" / "FishingAutomation" / "dungeon" / "AlteringScreen.cs"


class AiUiLogContract(unittest.TestCase):
    def test_status_updates_are_logged_with_dedup(self):
        source = MAIN.read_text(encoding="utf-8")
        self.assertIn('_log.Write("[AI 감시] " + text);', source)
        self.assertIn('_lastAiLoggedStatus', source)
        self.assertIn('if (!string.Equals(_lastAiLoggedStatus, text, StringComparison.Ordinal))', source)

    def test_closing_mabiauto_ends_watcher(self):
        main = MAIN.read_text(encoding="utf-8")
        host = HOST.read_text(encoding="utf-8")
        self.assertIn('_aiHost?.Dispose();', main)
        self.assertIn('child.Kill(entireProcessTree: true)', host)
        self.assertIn('프로그램 종료', main)

    def test_mcp_result_not_raw_error_is_logged(self):
        host = HOST.read_text(encoding="utf-8")
        self.assertIn('message.StartsWith("처리 시작:"', host)
        self.assertIn('eventName.StartsWith("error_"', host)
        self.assertIn('Publish("AI 신규 오류 분석·테스트 진행 중 · " + eventName)', host)

    def test_receipt_visual_veto_remains_fail_closed(self):
        screen = SCREEN.read_text(encoding="utf-8")
        self.assertIn('IsVerifiedManagedCompletionOverlayAsync(', screen)
        self.assertIn('if (facilityForPolicy)', screen)
        self.assertIn('WaitForReceiptFacilityReturnAsync(', screen)
        self.assertIn('int? after = await TryFacilityWorkCountAsync(plan, ct);', screen)
        self.assertIn('if (after != 0) return false;', screen)


if __name__ == "__main__":
    unittest.main()
