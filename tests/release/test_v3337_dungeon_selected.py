import unittest
from pathlib import Path

ENGINE = (Path(__file__).resolve().parents[2] /
          "source/FishingAutomation/dungeon/ScenarioEngine.cs").read_text(encoding="utf-8")


class DungeonSelectedRegression(unittest.TestCase):
    def test_fallback_requires_entry_and_two_matching_button_frames(self):
        entry = ENGINE.split("private async Task VerifyChallengeBeforeEntryAsync", 1)[1]
        condition = "if (!selected.Found && !challenge.Found && enter.Found && entryKeySafe.Contains(enter.Center))"
        markers = [condition, "if (visualSelected)", "using var confirmFrame",
                   "!freshEnter.Found", "freshChallenge.Found || !freshSelected.Found",
                   "!freshSelected.Bounds.Contains(selected.Center)",
                   "!selected.Bounds.Contains(freshSelected.Center)",
                   "visualSelectedClicked = true;", "_input.ClickClientPoint(_hwnd, selected.Center)"]
        positions = [entry.index(marker) for marker in markers]
        self.assertEqual(positions, sorted(positions))
        self.assertIn("if (!visualSelectedClicked && now - lastSelectedClick >= 1200)", entry)
        self.assertIn("if (readyConsecutive >= RequiredReadyFrames)", entry[:positions[0]])

    def test_recovery_reuses_position_checked_template(self):
        recovery = ENGINE.split("private async Task<int?> DetectKnownDungeonRecoveryStepAsync", 1)[1]
        self.assertIn("selected = await DetectDungeonSelectedVisualAsync(frame, ct);",
                      recovery.split("if (selected.Found || challenge.Found || enter.Found)", 1)[0])
        self.assertIn('DetectAsync("selected_red_visual", frame, ct)', ENGINE)
        self.assertIn("DungeonSelectedClickArea.Contains(selected.Center)", ENGINE)
