"""Fail-closed contract for the 2026-10-15 live false-clear incident.

Behavioral positive/negative pairs also run in tests/combined-scheduler.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
ENGINE = ROOT / "source/FishingAutomation/dungeon/ScenarioEngine.cs"
TIMEOUT = ROOT / "source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs"


class FalseClearSafetyTests(unittest.TestCase):
    def test_clear_needs_two_independent_signals(self):
        code = ENGINE.read_text(encoding="utf-8-sig")
        self.assertIn("AbyssClearEvidencePolicy.Next(", code)
        self.assertIn("AbyssClearEvidencePolicy.Confirmed(", code)
        self.assertIn("clearTitle.Found, touch.Found", code)
        self.assertNotIn("clear_title_2x_safe_fallback", code)
        self.assertNotIn("clear_touch_safe_fallback", code)
        self.assertNotIn("clear_touch_safe_retry", code)

    def test_no_unverified_click_while_title_is_only_candidate(self):
        code = ENGINE.read_text(encoding="utf-8-sig")
        self.assertIn("AbyssClearPairedEvidencePending", code)
        self.assertIn("!AbyssClearPairedEvidencePending && await CheckMonitorsAsync", code)
        self.assertNotIn("AbyssClearSafeTouch", code)

    def test_timeout_does_not_click_on_touch_alone(self):
        code = TIMEOUT.read_text(encoding="utf-8-sig")
        start = code.index("private async Task<bool> HandleAbyssLateClearAsync")
        end = code.index("private async Task ExitAbyssAfterCombatTimeoutAsync", start)
        body = code[start:end]
        self.assertIn("DetectAbyssConfirmedClearAsync(frame, ct)", body)
        self.assertNotIn('if (touch.Found)', body)
        self.assertNotIn("AdvanceAbyssClearScreenAsync(touch, ct)", body)


if __name__ == "__main__":
    unittest.main()
