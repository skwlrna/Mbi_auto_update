"""V3.3.25: 17:52 ResultConfirmed retry after OCR/chest/heading false negatives."""
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
D="source/FishingAutomation/dungeon/"
def read(p):return (ROOT/p).read_text(encoding="utf-8")

class ConfirmedVisualRetry(unittest.TestCase):
    def test_not_available_for_initial_result_or_scheduled_exit(self):
        retry=read(D+"ScenarioEngine.AbyssRetry.cs")
        helper=retry.split("private async Task<DetectionResult> DetectAbyssConfirmedResultVisualRetryAsync(",1)[1].split("private int AbyssCombatStepIndex",1)[0]
        self.assertIn("GetAbyssFlowState() != AbyssFlowState.ResultConfirmed",helper)
        for reject in ["AbyssMissionCompletePolicy.HasVisualEvidence(frame)",
                       '"abyss_exit_confirm_title"', '"abyss_enter"',
                       "clearTitle.Found || clearTouch.Found",
                       "HasAbyssActiveCombatHudAsync(frame, ct)",
                       "TryDetectAbyssResultButtonRow(frame, out var retryRoi)"]:
            self.assertIn(reject,helper)
        self.assertNotIn("FindCompactLabelAsync",helper)
        self.assertNotIn("_input.",helper)
        self.assertNotIn("AbyssTransitionTo(",helper)
        self.assertNotIn("DetectAbyssConfirmedResultVisualRetryAsync",read(D+"ScenarioEngine.AbyssSchedulerExit.cs"))
        self.assertNotIn("DetectAbyssConfirmedResultVisualRetryAsync",read(D+"ScenarioEngine.AbyssTimeout.cs"))
        self.assertNotIn("DetectAbyssConfirmedResultVisualRetryAsync",read(D+"ScenarioEngine.NetworkRecovery.cs"))

    def test_retry_has_initial_two_frames_and_final_two_fresh_frames(self):
        retry=read(D+"ScenarioEngine.AbyssRetry.cs")
        b=retry.split("private async Task RetryAbyssResultAsync(",1)[1].split("private async Task<bool> HasAbyssRecruitmentEvidenceAsync(",1)[0]
        self.assertIn("var retry = await DetectAbyssResultRetryAsync(frame, ct);",b)
        self.assertIn("retry = await DetectAbyssConfirmedResultVisualRetryAsync(frame, ct)",b)
        self.assertIn("freshRetry = await DetectAbyssConfirmedResultVisualRetryAsync(fresh, ct)",b)
        self.assertIn("confirmedRetry = await DetectAbyssConfirmedResultVisualRetryAsync(confirmedFrame, ct)",b)
        self.assertLess(b.index("TryRecoverVerifiedClearTouchAfterResultAsync(frame, ct)"),
                        b.index("var retry = await DetectAbyssResultRetryAsync(frame, ct);"))
        self.assertIn("retry.Bounds.IntersectsWith(previous.Bounds)",b)
        self.assertIn("freshRetry.Bounds.IntersectsWith(retry.Bounds)",b)
        self.assertIn("confirmedRetry.Bounds.IntersectsWith(freshRetry.Bounds)",b)
        self.assertIn("ConfirmAbyssFixedButtonClick(fresh, exit: false)",b)
        self.assertIn("ConfirmAbyssFixedButtonClick(confirmedFrame, exit: false)",b)
        self.assertIn("VerifyAbyssClickClientGeometry(_hwnd, confirmedFrame.Size)",b)
        self.assertIn("consecutiveCombatHudFrames >= 3",b)

    def test_runtime_scenario_and_prior_safety_gates(self):
        harness=read("tests/retry-flow/Program.cs")
        self.assertIn("17:52 confirmed result 3-button visual proof succeeds",harness)
        self.assertIn("ForceRetryOcrMiss",harness)
        self.assertIn("fallback never authorizes click with actual combat HUD",harness)
        for p in (".github/workflows/ci.yml",".github/workflows/release-v3325-auto-update.yml"):
            w=read(p)
            for t in ("test_v3325_confirmed_result_visual_retry.py",
                      "test_v3324_shared_clear_evidence.py",
                      "test_v3323_result_reward_timeout.py",
                      "test_v3322_combat_result_rescue.py",
                      "test_v3321_clear_overlay_veto.py",
                      "test_v3320_combat_result_veto.py"):
                self.assertIn(t,w)
            self.assertIn("tests/retry-flow/Regression.csproj",w)

if __name__=="__main__":unittest.main()
