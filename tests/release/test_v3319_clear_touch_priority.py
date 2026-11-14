"""V3.3.19 clear-touch overlays cannot be promoted to result, and false result may safely recover."""
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
def read(path): return (ROOT / path).read_text(encoding="utf-8")

class ClearTouchPriority(unittest.TestCase):
    def test_completed_result_veto_precedes_green_buttons_and_rewards_ocr(self):
        s = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        method = s.split("private async Task<bool> HasAbyssCompletedResultEvidenceAsync(", 1)[1].split(
            "private async Task<bool> ConfirmAbyssCompletedResultAsync(", 1)[0]
        self.assertIn('"abyss_dungeon_clear_visual", frame, ct', method)
        self.assertIn('"abyss_touch_screen", frame, ct', method)
        self.assertIn('touchArea.Contains(pendingTouch.Bounds)', method)
        self.assertIn('pendingTitle.Found && pendingTouch.Found', method)
        self.assertLess(method.index('pendingTitle.Found && pendingTouch.Found'),
                        method.index('TryDetectAbyssResultButtonRow(frame, out _)'))
        self.assertLess(method.index('pendingTitle.Found && pendingTouch.Found'),
                        method.index('"전투 시간", ct'))
        self.assertIn('return false;', method)
        self.assertIn('AbyssMissionCompletePolicy.HasVisualEvidence(frame)', method)

    def test_result_failure_can_resume_only_verified_clear_touch_once(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        block = retry.split('private async Task<bool> TryRecoverVerifiedClearTouchAfterResultAsync(',1)[1].split(
            'private async Task RetryAbyssResultAsync(',1)[0]
        self.assertIn('GetAbyssFlowState() != AbyssFlowState.ResultConfirmed',block)
        self.assertIn('!first.Title.Found || !first.Touch.Found',block)
        self.assertIn('!second.Title.Found || !second.Touch.Found',block)
        self.assertIn('safe.Contains(first.Touch.Bounds)',block)
        self.assertIn('secondSafe.Contains(second.Touch.Bounds)',block)
        self.assertIn('first.Title.Bounds.IntersectsWith(second.Title.Bounds)',block)
        self.assertIn('first.Touch.Bounds.IntersectsWith(second.Touch.Bounds)',block)
        self.assertIn('secondFrame.Size != firstFrame.Size',block)
        self.assertIn('VerifyAbyssClickClientGeometry(_hwnd, secondFrame.Size)',block)
        self.assertLess(block.index('first.Touch.Bounds.IntersectsWith(second.Touch.Bounds)'),
                        block.index('AbyssRestoreVerifiedClearTouchFromPrematureResult('))
        self.assertIn('await AdvanceAbyssClearScreenAsync(second.Touch, ct)',block)
        self.assertNotIn('_input.ClickClientPoint(',block)
        self.assertNotIn('_input.TapScanCode(',block)
        flow = retry.split('private async Task RetryAbyssResultAsync(',1)[1].split(
            'private async Task<bool> HasAbyssRecruitmentEvidenceAsync(',1)[0]
        self.assertIn('clearTouchRecoveries == 0', flow)
        # V3.3.21: the recovery must run *before* even a visually false
        # retry candidate; the old !retry.Found guard caused the 22:14 stall.
        self.assertNotIn('!retry.Found && clearTouchRecoveries == 0', flow)
        self.assertLess(flow.index('await TryRecoverVerifiedClearTouchAfterResultAsync(frame, ct)'),
                        flow.index('var retry = await DetectAbyssResultRetryAsync(frame, ct)'))
        self.assertIn('GetAbyssFlowState() == AbyssFlowState.ResultConfirmed',flow)
        self.assertIn('await TryRecoverVerifiedClearTouchAfterResultAsync(frame, ct)',flow)
        self.assertIn('clearTouchRecoveries++',flow)
        self.assertIn('timer.Restart()',flow)
        self.assertIn('ConfirmAbyssFixedButtonClick(confirmedFrame, exit: false)',flow)

    def test_flow_reversal_is_explicit_and_not_global(self):
        state = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssState.cs")
        helper = state.split('private void AbyssRestoreVerifiedClearTouchFromPrematureResult(',1)[1].split(
            'private void AbyssRequireState(',1)[0]
        self.assertIn('_abyssFlowState != AbyssFlowState.ResultConfirmed',helper)
        self.assertIn('_abyssFlowState = AbyssFlowState.ClearConfirmed',helper)
        self.assertNotIn('(AbyssFlowState.ResultConfirmed, AbyssFlowState.ClearConfirmed) => true',state)
        self.assertIn('(AbyssFlowState.ResultConfirmed, AbyssFlowState.RetryClicked) => true',state)

    def test_no_change_to_original_touch_and_v318_timeout_guard(self):
        e=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        self.assertIn('await AdvanceAbyssClearScreenAsync(found, ct)',e)
        self.assertIn('private async Task AdvanceAbyssClearScreenAsync(',e)
        self.assertIn('await WaitForAbyssClearScreenGoneAsync(ct)',e)
        self.assertIn('await ConfirmAbyssCompletedResultAsync(frame, ct)',e)
        timeout=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs")
        self.assertIn('await WaitForAbyssTimeoutExitDialogAsync(',timeout)
        self.assertIn('if (await ConfirmAbyssCompletedResultAsync(afterCancel, ct))',timeout)
        scheduler=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssSchedulerExit.cs")
        self.assertIn('ConfirmAbyssFixedButtonClick(fresh, exit: true)',scheduler)

if __name__ == '__main__': unittest.main()
