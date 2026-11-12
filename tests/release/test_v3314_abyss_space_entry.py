"""V3.3.14: first Abyss entry uses the advertised Space key, never mouse."""
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

class AbyssInitialEntrySpaceRegression(unittest.TestCase):
    def test_abyss_entry_revalidates_then_taps_space_without_clicking(self):
        code = (ROOT / "source/FishingAutomation/dungeon/ScenarioEngine.cs").read_text(encoding="utf-8")
        marker = 'if (IsAbyss && step.Target.Equals("abyss_enter", StringComparison.OrdinalIgnoreCase))'
        self.assertEqual(code.count(marker), 1)
        # Inspect only the production wait_click action, not other Abyss click stages.
        entry = code.split(marker, 1)[1].split('await Task.Delay(_settings.ClickSettleMs, ct);', 1)[0]
        for required in (
            'await BeginInventoryLootRoundAsync(ct);',
            'using var entryFrame = await CaptureGameWindowAsync(ct);',
            'found = await _detector.DetectAsync(step.Target, entryFrame, ct);',
            'if (!found.Found) continue;',
            '_hwnd = await ResolveRequiredGameWindowAsync(ct);',
            'NativeMethods.SetForegroundWindow(_hwnd);',
            '_input.TapScanCode(0x39, ct);',
        ):
            self.assertIn(required, entry)
        self.assertLess(entry.index('if (!found.Found) continue;'), entry.index('_input.TapScanCode(0x39, ct);'))
        before_else = entry.split('                            else\n', 1)[0]
        self.assertNotIn('_input.ClickClientPoint(', before_else)
        self.assertIn('_input.ClickClientPoint(_hwnd, found.Center);', entry)
        self.assertIn('[어비스 입장] 버튼 재확인 -> Space 입력', entry)

    def test_combat_timer_requires_actual_dungeon_evidence(self):
        timeout = (ROOT / "source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs").read_text(encoding="utf-8")
        self.assertIn('await HasAbyssCombatEvidenceAsync(frame, ct)', timeout)
        # V3.3.15 still needs three consecutive dungeon frames but starts
        # the combat clock on the third one, instead of skipping the first two.
        self.assertTrue('if (++gone < 3)' in timeout or 'if (++gone >= 3)' in timeout)
        self.assertTrue(
            '어비스 입장 화면 이탈을 확인하지 못해 전투 타이머를 시작하지 않습니다.' in timeout
            or '60초 내 어비스 입장 화면 이탈과 실제 던전 내부 3프레임을 확인하지 못해 전투 타이머를 시작하지 않습니다.' in timeout
        )

    def test_non_abyss_space_and_other_mouse_clicks_stay_unmodified(self):
        code = (ROOT / "source/FishingAutomation/dungeon/ScenarioEngine.cs").read_text(encoding="utf-8")
        self.assertIn('Log?.Invoke($"[던전 입장] 도전 상태 + 오른쪽 입장하기 확인 -> Space 입력', code)
        self.assertIn('_input.TapScanCode(0x39);', code)
        self.assertIn('_input.ClickClientPoint(_hwnd, found.Center);', code)

if __name__ == "__main__":
    unittest.main()
