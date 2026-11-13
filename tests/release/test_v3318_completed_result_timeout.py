"""V3.3.18: confirmed finished rewards must preempt the combat timeout and all exit inputs."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def read(path): return (ROOT/path).read_text(encoding="utf-8")

class CompletedAbyssRewardRecovery(unittest.TestCase):
    def test_combat_wait_checks_result_before_clear_or_global_monitor(self):
        s=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        loop=s.split('while (IsAbyss && step.Target == "abyss_touch_screen"',1)[1].split('DiagnosticObserveDetection(step.Target, found);',1)[0]
        self.assertIn('lastAbyssCompletedResultProbeAt >= 1250',loop)
        self.assertIn('await ConfirmAbyssCompletedResultAsync(frame, ct)',loop)
        self.assertIn('MarkAbyssCompletedResult("전투 대기 중 완료 결과 화면 2프레임 확인");',loop)
        self.assertLess(loop.index('await ConfirmAbyssCompletedResultAsync(frame, ct)'),
                        loop.index('await DetectAbyssConfirmedClearAsync(frame, ct)'))
        self.assertLess(loop.index('await ConfirmAbyssCompletedResultAsync(frame, ct)'),
                        loop.index('await CheckMonitorsAsync(frame, ct)'))
        self.assertIn('return; // Let the existing result step decide Retry vs Processing Exit.',loop)
        self.assertIn('if (_scheduledResultExit && await ShouldExitScheduledAbyssAfterRoundAsync(ct))',s)

    def test_rewards_have_two_independent_anchored_labels_two_fresh_frames_and_no_click(self):
        s=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        self.assertIn('AbyssFinishedTimeRoi = new(320, 222, 270, 80)',s)
        self.assertIn('AbyssFinishedLootRoi = new(295, 450, 265, 100)',s)
        self.assertIn('TryDetectAbyssResultButtonRow(frame, out _)',s)
        self.assertIn('AbyssMissionCompletePolicy.HasVisualEvidence(frame)',s)
        self.assertIn('frame, timeArea, "전투 시간", ct',s)
        self.assertIn('frame, lootArea, "발견한 전리품", ct',s)
        self.assertIn('timeArea.Contains(time.Bounds)',s)
        self.assertIn('lootArea.Contains(loot.Bounds)',s)
        self.assertIn('var fresh = await CaptureGameWindowAsync(ct)',s)
        self.assertIn('HasAbyssCompletedResultEvidenceAsync(fresh, ct)',s)
        self.assertNotIn('ClickClientPoint(',s)
        self.assertNotIn('TapScanCode(',s)
        self.assertIn('AbyssFlowState.ResultConfirmed',s)
        self.assertIn('강제 퇴장 취소',s)

    def test_exit_modal_has_guarded_esc_reinspect_and_space_only_for_proven_combat(self):
        s=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs")
        helper=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        self.assertIn('abyss_exit_confirm_title',helper)
        self.assertIn('frame, exitArea, "나가기", ct',helper)
        self.assertIn('exitArea.Contains(templated.Bounds)',helper)
        self.assertIn('oldTitle.Bounds.IntersectsWith(title.Bounds)',helper)
        self.assertIn('oldExit.Bounds.IntersectsWith(exitLabel.Bounds)',helper)
        self.assertIn('await ConfirmAbyssCompletedResultAsync(frame, ct)',helper)
        self.assertNotIn('foreach (var action in new[]',s)
        self.assertIn('ClickAbyssTimeoutLeaveControlAsync(',s)
        self.assertIn('WaitForAbyssTimeoutExitDialogAsync(',s)
        self.assertIn('_input.TapScanCode(0x01, ct);',s)
        self.assertIn('if (await IsAbyssTimeoutExitDialogVisibleAsync(ct))',s)
        self.assertIn('"abyss_leave_dungeon", afterCancel, ct',s)
        self.assertIn('"abyss_leave_dungeon", freshCombat, ct',s)
        self.assertIn('secondCombatLeaveIcon.Bounds.IntersectsWith(firstCombatLeaveIcon.Bounds)',s)
        self.assertNotIn('HasAbyssCombatEvidenceAsync(afterCancel, ct)',s)
        self.assertIn('_input.TapScanCode(0x39, ct);',s)
        self.assertIn('if (disappeared < 2)',s)
        self.assertIn('await WaitForAbyssHomeAfterNormalExitAsync(ct);',s)
        self.assertEqual(s.count('_input.TapScanCode(0x39, ct);'),2,
                         'one space for entry and one for verified incomplete timeout exit')
        self.assertEqual(s.count('_input.TapScanCode(0x01, ct);'),1,
                         'only one timeout-confirmation ESC is permitted')
        self.assertIn('DiagnosticObserveClick("abyss_timeout_leave", target.Center)',s)

    def test_existing_abyss_and_processing_safety_remains(self):
        e=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        retry=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        scheduler=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssSchedulerExit.cs")
        self.assertIn('await RetryAbyssResultAsync(ct);',e)
        self.assertIn('await ExitAbyssResultForScheduledProcessingAsync(ct);',e)
        self.assertIn('ConfirmAbyssFixedButtonClick(confirmedFrame, exit: false)',retry)
        self.assertIn('ConfirmAbyssFixedButtonClick(fresh, exit: true)',scheduler)
        self.assertIn('WaitForAbyssHomeAfterNormalExitAsync(ct)',scheduler)

if __name__=="__main__":
    unittest.main()
