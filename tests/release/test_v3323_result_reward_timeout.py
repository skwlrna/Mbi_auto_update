"""V3.3.23: 2026-11-15 02:28 real reward screen was not recognized.

The 800x1000 screenshot showed the chest at x359..438, y120..189,
the loot heading near y420, and three separate green bottom buttons.
The previous loot OCR ROI began at y450 and missed the heading.
This policy regression protects the executable's bounded visual guard;
Windows compilation and the historic 22:58 combat tests run in CI.
"""
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(p): return (ROOT / p).read_text(encoding="utf-8")

class V3323RewardResultTimeout(unittest.TestCase):
    def test_incident_loot_heading_and_chest_are_in_safe_rois(self):
        completed = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        m = re.search(r"AbyssFinishedLootRoi = new\((\d+), (\d+), (\d+), (\d+)\)", completed)
        self.assertIsNotNone(m)
        x,y,w,h = map(int, m.groups())
        self.assertTrue(x <= 356 and x+w >= 444 and y <= 420 < y+h)
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        m = re.search(r"AbyssFinishedTreasureRoi = new\((\d+), (\d+), (\d+), (\d+)\)", retry)
        self.assertIsNotNone(m)
        x,y,w,h = map(int, m.groups())
        self.assertTrue(x <= 359 and x+w >= 438 and y <= 120 and y+h >= 189)

    def test_chest_proof_cannot_bypass_three_button_or_combat_guards(self):
        retry = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs")
        body = retry.split("private async Task<DetectionResult> DetectAbyssResultRetryAsync(",1)[1].split(
            "private async Task<Dictionary<string, DetectionResult>>",1)[0]
        self.assertLess(body.index('HasAbyssActiveCombatHudAsync(frame, ct)'), body.index('TryDetectAbyssResultButtonRow(frame, out var visualRetry)'))
        self.assertLess(body.index("if (!visualSignal)"),body.index('DetectAsync("abyss_treasure_chest", frame, ct)'))
        self.assertIn("treasureArea.Contains(treasure.Bounds)",body)
        self.assertIn("if (!rewardsTextProof)",body)
        self.assertIn('"다시 하기", ct',body)
        self.assertIn("retryCenterGreen < 0.25",retry)
        self.assertIn('"visual_result_button_row_anchored_chest"',body)

    def test_normal_and_timeout_path_require_two_fresh_frames(self):
        result = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssCompletedResult.cs")
        timeout = read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs")
        engine = read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        self.assertIn("Math.Max(700, _settings.PollIntervalMs)",result)
        self.assertIn("bool confirmed = await HasAbyssCompletedResultEvidenceAsync(fresh, ct);",result)
        self.assertLess(timeout.index("ConfirmAbyssCompletedResultAsync(frame, ct)"),timeout.index('DetectAsync("abyss_leave_dungeon", frame, ct)'))
        self.assertIn('MarkAbyssCompletedResult("02:28: 퇴장 아이콘 없음',timeout)
        self.assertIn("if (await ConfirmAbyssCompletedResultAsync(finalFrame, ct))",timeout)
        self.assertIn("if (await ConfirmAbyssCompletedResultAsync(frame, ct))",engine)
        self.assertIn("AbyssTransitionTo(AbyssFlowState.ResultConfirmed",engine)

    def test_ci_release_preserve_prior_active_combat_veto(self):
        ci=read(".github/workflows/ci.yml")
        release=read(".github/workflows/release-v3323-auto-update.yml")
        for file in ("test_v3323_result_reward_timeout.py","test_v3322_combat_result_rescue.py",
                     "test_v3321_clear_overlay_veto.py","test_v3320_combat_result_veto.py"):
            self.assertIn(file,ci)
            self.assertIn(file,release)
        self.assertIn("No successful production CI for current main SHA",release)
        self.assertIn("main changed since CI; abort release",release)
        self.assertIn("gh release create V3.3.23",release)

if __name__ == "__main__":
    unittest.main()
