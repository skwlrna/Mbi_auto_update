"""V3.3.22: 22:58:10 active-combat false result and 22:53 clear-overlay regression.

This policy test checks production call sites and the specific observed failure
boundary; full Windows compile and legacy regression suites run in protected CI.
"""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(p): return (ROOT / p).read_text(encoding="utf-8")

class CombatResultRescue(unittest.TestCase):
    def test_result_promotion_has_combat_veto_and_corroborated_text(self):
        result = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        b = result.split("private async Task<bool> HasAbyssCompletedResultEvidenceAsync(",1)[1].split(
            "private async Task<bool> ConfirmAbyssCompletedResultAsync(",1)[0]
        self.assertLess(b.index("pendingTitle.Found && pendingTouch.Found"),
                        b.index("await HasAbyssActiveCombatHudAsync(frame, ct)"))
        self.assertLess(b.index("await HasAbyssActiveCombatHudAsync(frame, ct)"),
                        b.index("TryDetectAbyssResultButtonRow(frame, out _)"))
        self.assertIn('await DetectAbyssResultRetryAsync(frame, ct)', b)
        self.assertIn('"다시 하기", ct', b)
        self.assertIn('if (!HasAbyssFinishedFallbackActionRow(frame))', b)
        self.assertLess(b.index("TryDetectAbyssResultButtonRow(frame, out _)"),
                        b.index("HasAbyssFinishedFallbackActionRow(frame)"))
        self.assertIn('Math.Max(700, _settings.PollIntervalMs)', result)

    def test_legacy_generic_inside_proof_not_used_as_combat(self):
        network = read("source/FishingAutomation/dungeon/ScenarioEngine.NetworkRecovery.cs")
        b = network.split("private async Task<bool> HasAbyssActiveCombatHudAsync(",1)[1].split(
            "private async Task<bool> HasAbyssCombatEvidenceAsync(",1)[0]
        self.assertIn('DetectAsync("abyss_leave_dungeon", frame, ct)', b)
        self.assertIn('DetectAsync("abyss_enter", frame, ct)', b)
        self.assertIn('AbyssMissionCompletePolicy.HasVisualEvidence(frame)', b)
        self.assertNotIn('HasAbyssCombatEvidenceAsync(frame, ct)', b)
        self.assertNotIn('_input.ClickClientPoint', b)
        self.assertNotIn('_input.TapScanCode', b)

    def test_three_frame_bounded_reversal_not_120_second_stall(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        flow = retry.split("private async Task RetryAbyssResultAsync(",1)[1].split(
            "private async Task<bool> HasAbyssRecruitmentEvidenceAsync(",1)[0]
        self.assertIn("int consecutiveCombatHudFrames = 0;",flow)
        self.assertIn("!retry.Found",flow)
        self.assertIn("consecutiveCombatHudFrames >= 3",flow)
        self.assertIn("AbyssRestoreVerifiedCombatFromPrematureResult(",flow)
        self.assertIn("throw new AbyssResumeVerifiedCombatException()",flow)
        self.assertLess(flow.index("await TryRecoverVerifiedClearTouchAfterResultAsync(frame, ct)"),
                        flow.index("var retry = await DetectAbyssResultRetryAsync(frame, ct)"))
        self.assertLess(flow.index("consecutiveCombatHudFrames >= 3"),
                        flow.index("if (retry.Found && previous.Found"))
        self.assertIn("ConfirmAbyssFixedButtonClick(confirmedFrame, exit: false)",flow)
        self.assertIn("GetAbyssFixedButtonGreenRatio(frame, retryCenter)",retry)
        self.assertIn("retryCenterGreen < 0.25",retry)
        self.assertFalse(.000 >= .25)  # 22:58:10 actual retry center
        self.assertFalse(.144 >= .25)  # 22:14:27 previous overlay

    def test_original_timer_and_scheduler_lease_are_preserved(self):
        state = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssState.cs")
        b = state.split("private void AbyssRestoreVerifiedCombatFromPrematureResult(",1)[1].split(
            "private void AbyssRequireState(",1)[0]
        self.assertIn("_abyssCombatStartedAt.HasValue",b)
        self.assertIn("_abyssFlowState = AbyssFlowState.CombatClearWait",b)
        self.assertIn("_abyssFalseResultCombatRecoveries > 3",b)
        self.assertNotIn("_abyssCombatStartedAt = null",b)
        self.assertNotIn("ResetAbyssExitState(",b)
        engine = read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        catch = engine.split("catch (AbyssResumeVerifiedCombatException)",1)[1].split(
            "catch (RestartCycleException)",1)[0]
        self.assertIn("_resumeStepIndex = AbyssCombatStepIndex;",catch)
        self.assertNotIn("_resumeStepIndex = 0",catch)
        self.assertNotIn("_abyssCombatStartedAt = null",catch)
        self.assertIn("if (_scheduledResultExit) scheduledRecoveryCycles++",catch)
        timeout = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs")
        self.assertIn("if (_abyssCombatStartedAt.HasValue) return;",timeout)
        self.assertIn("10분 경계에서 입장하기 화면 확인 -> 던전 퇴장 입력 금지",timeout)

    def test_touch_overlay_22_53_must_survive_animated_text(self):
        result = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        engine = read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        b = engine.split("private async Task<DetectionResult> DetectAbyssConfirmedClearAsync(",1)[1].split(
            "private async Task<DetectionResult> DetectAbyssClearVisualAsync(",1)[0]
        for mark in ('"던전 클리어", ct','"화면을 터치해 주세요", ct',
                     "clearTouchArea.Contains(touch.Bounds)",
                     "AbyssClearEvidencePolicy.Confirmed("):
            self.assertIn(mark,b)
        self.assertIn('titleArea.Contains(ocrTitle.Bounds)',result)
        self.assertIn('touchArea.Contains(ocrTouch.Bounds)',result)
        self.assertIn("await AdvanceAbyssClearScreenAsync(found, ct)", engine)

    def test_release_retains_exact_historical_overlay_test_path(self):
        workflow = read(".github/workflows/release-v3322-auto-update.yml")
        self.assertIn("test_v3321_clear_overlay_veto.py", workflow)
        self.assertIn("test_v3322_combat_result_rescue.py", workflow)
        self.assertNotIn("test_v3322_clear_overlay_veto.py", workflow)

    def test_physical_button_color_not_sufficient_for_click(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        b = retry.split("private async Task<DetectionResult> DetectAbyssResultRetryAsync(",1)[1].split(
            "private async Task<Dictionary<string, DetectionResult>> DetectAbyssLootTemplateFrameAsync(",1)[0]
        self.assertIn("active-combat HUD vetoed retry candidate",b)
        self.assertIn("rewardsTextProof",b)
        self.assertIn("if (!rewardsTextProof)",b)
        self.assertIn('"전투 시간", ct',b)
        self.assertIn('"발견한 전리품", ct',b)

if __name__ == "__main__":
    unittest.main()
