"""V3.3.21: replay the 22:14:27 clear-overlay false-result decision boundary."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(path): return (ROOT / path).read_text(encoding="utf-8")


class AbyssClearOverlayVetoRegression(unittest.TestCase):
    def test_measured_2214_clear_overlay_cannot_pass_result_button_row(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        method = retry.split("private bool TryDetectAbyssResultButtonRow(", 1)[1].split(
            "private int AbyssCombatStepIndex", 1)[0]
        self.assertIn("GetAbyssFixedButtonGreenRatio(frame, retryCenter)", method)
        self.assertIn("retryCenterGreen < 0.25", method)
        self.assertIn("AbyssResultLayout.FixedButtonCenter(false, frame.Size)", method)
        self.assertIn("exitGreen < 0.18", method)
        self.assertIn("retryGreen < 0.18", method)
        self.assertIn("otherGreen < 0.18", method)
        # The actual 2026-11-14 22:14:27 center patch: 0.144.
        self.assertFalse(.144 >= .25)
        self.assertTrue(.47 >= .25)  # control: a genuine solid green button

    def test_verified_clear_touch_must_preempt_any_retry_candidate(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        flow = retry.split("private async Task RetryAbyssResultAsync(", 1)[1].split(
            "private async Task<bool> HasAbyssRecruitmentEvidenceAsync(", 1)[0]
        self.assertLess(
            flow.index("await TryRecoverVerifiedClearTouchAfterResultAsync(frame, ct)"),
            flow.index("var retry = await DetectAbyssResultRetryAsync(frame, ct)")
        )
        self.assertIn("clearTouchRecoveries == 0", flow)
        self.assertIn("timer.Restart()", flow)
        self.assertIn("ConfirmAbyssFixedButtonClick(confirmedFrame, exit: false)", flow)

    def test_animated_title_and_touch_have_two_frame_ocr_fallback(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        recover = retry.split("private async Task<bool> TryRecoverVerifiedClearTouchAfterResultAsync(", 1)[1].split(
            "private async Task RetryAbyssResultAsync(", 1)[0]
        for required in ('"던전 클리어", ct', '"화면을 터치해 주세요", ct',
                         "secondFrame.Size != firstFrame.Size",
                         "first.Title.Bounds.IntersectsWith(second.Title.Bounds)",
                         "first.Touch.Bounds.IntersectsWith(second.Touch.Bounds)",
                         "touchArea.Contains(ocrTouch.Bounds)",
                         "titleArea.Contains(ocrTitle.Bounds)",
                         "AbyssRestoreVerifiedClearTouchFromPrematureResult(",
                         "await AdvanceAbyssClearScreenAsync(second.Touch, ct)"):
            self.assertIn(required, recover)
        self.assertNotIn("_input.ClickClientPoint(", recover)
        self.assertNotIn("_input.TapScanCode(", recover)

    def test_10_minute_timeout_and_enter_path_remain_untouched(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        timeout = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs")
        completed = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        self.assertIn('if (TryDetectAbyssResultButtonRow(frame, out _))', completed)
        self.assertIn("await ConfirmAbyssCompletedResultAsync(frame, ct)", timeout)
        self.assertIn('10분 경계에서 입장하기 화면 확인 -> 던전 퇴장 입력 금지', timeout)
        self.assertIn("GetAbyssFixedButtonGreenRatio(frame, point)", retry)

if __name__ == "__main__": unittest.main()
