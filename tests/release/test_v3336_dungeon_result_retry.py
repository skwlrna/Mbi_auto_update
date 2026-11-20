import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
ENGINE = (ROOT / "source/FishingAutomation/dungeon/ScenarioEngine.cs").read_text(encoding="utf-8")


class DungeonRetryRegression(unittest.TestCase):
    def test_result_screen_and_center_button_are_required_without_exact_ocr(self):
        detector = ENGINE.split("private async Task<DetectionResult> FindDungeonResultRetryAsync", 1)[1]
        detector = detector.split("private async Task VerifyChallengeBeforeEntryAsync", 1)[0]
        self.assertIn("AbyssRewardVisualPolicy.TryDetect", detector)
        self.assertIn('DetectAsync("retry", frame, ct)', detector)
        self.assertIn("retryArea.Contains(retry.Center)", detector)
        self.assertNotIn("retry_ocr_strict", ENGINE)

    def test_normal_step_revalidates_before_click_and_checks_screen_after(self):
        step = ENGINE.split('if (step.Target.Equals("retry",', 1)[1]
        step = step.split('if (step.Target.Equals("abyss_touch_screen"', 1)[0]
        ordered = [
            "TryDetectDungeon(frame",
            "RetryClickDelay.WaitAsync",
            "CaptureGameWindowAsync",
            "FindDungeonResultRetryAsync",
            "IntersectsWith(found.Bounds)",
            "retryGuardSelected.Found || retryGuardChallenge.Found || retryGuardEnter.Found",
            "_input.ClickClientPoint",
            "TryDetectDungeon(afterRetryFrame",
            "throw new TimeoutException",
        ]
        positions = [step.index(item) for item in ordered]
        self.assertEqual(positions, sorted(positions))
        self.assertEqual(step.count("_input.ClickClientPoint"), 1)
        self.assertIn('FindDungeonStepIndex("retry")', ENGINE)
