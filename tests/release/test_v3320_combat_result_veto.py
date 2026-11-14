"""V3.3.20: active Abyss combat must not be mistaken for the rewards panel."""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def source(path): return (ROOT/path).read_text(encoding="utf-8")

class CombatFalseResultVeto(unittest.TestCase):
    def test_rewards_ocr_cannot_promote_without_real_action_row(self):
        code = source("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        method = code.split("private async Task<bool> HasAbyssCompletedResultEvidenceAsync(",1)[1].split(
            "private async Task<bool> ConfirmAbyssCompletedResultAsync(",1)[0]
        )
        self.assertIn('TryDetectAbyssResultButtonRow(frame, out _)', method)
        self.assertIn('"전투 시간", ct', method)
        self.assertIn('"발견한 전리품", ct', method)
        self.assertIn('if (!HasAbyssFinishedFallbackActionRow(frame))\n            return false;', method)
        self.assertLess(method.index('TryDetectAbyssResultButtonRow(frame, out _)'),
                        method.index('if (!HasAbyssFinishedFallbackActionRow(frame))'))
        self.assertIn('return !(await _detector.DetectAsync("abyss_exit_confirm_title", frame, ct)).Found;',method)

    def test_bounded_button_geometry_and_conservative_thresholds(self):
        code = source("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        check = code.split("private static bool HasAbyssFinishedFallbackActionRow(",1)[1].split(
            "private async Task<bool> HasAbyssCompletedResultEvidenceAsync(",1)[0]
        )
        for anchor in ('AbyssRetryButtonRoi','AbyssExitButtonRoi','AbyssOtherButtonRoi',
                       'AbyssResultLayout.FixedButtonCenter(false, frame.Size)',
                       'GetAbyssFixedButtonGreenRatio(frame, retryCenter)'):
            self.assertIn(anchor,check)
        self.assertIn('aspect < 0.74 || aspect > 0.92',check)
        self.assertIn('retryGreen >= 0.28', check)
        self.assertIn('centerGreen >= 0.30', check)
        self.assertIn('Math.Max(leftGreen, rightGreen) >= 0.25',check)
        def safe(exit_green,retry_green,other_green,center_green):
            return retry_green>=0.28 and center_green>=0.30 and max(exit_green,other_green)>=0.25
        self.assertFalse(safe(.089,.201,.095,.90))  # 20:06 actual 2026-11-14
        self.assertFalse(safe(.152,.119,.209,.90))  # 20:46 actual 2026-11-14
        self.assertTrue(safe(.48,.50,.47,.45))       # complete result, strong buttons

    def test_old_touch_timeout_and_safe_clicking_unchanged(self):
        retry=source("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        timeout=source("source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs")
        engine=source("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        self.assertIn('ConfirmAbyssFixedButtonClick(confirmedFrame, exit: false)',retry)
        self.assertIn('TryRecoverVerifiedClearTouchAfterResultAsync(frame, ct)',retry)
        self.assertIn('await ConfirmAbyssCompletedResultAsync(frame, ct)',engine)
        self.assertIn('await WaitForAbyssTimeoutExitDialogAsync(',timeout)

if __name__ == "__main__": unittest.main()
