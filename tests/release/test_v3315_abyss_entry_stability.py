"""V3.3.15: bounded Space retry only on stable observed entry, never false combat."""
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]

def read(p):
    return (ROOT/p).read_text(encoding="utf-8")

class AbyssEntryStability(unittest.TestCase):
    def test_first_space_requires_fresh_settled_entry(self):
        s=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        block=s.split('if (IsAbyss && step.Target.Equals("abyss_enter", StringComparison.OrdinalIgnoreCase))',1)[1].split("await Task.Delay(_settings.ClickSettleMs, ct);",1)[0]
        for required in ('await BeginInventoryLootRoundAsync(ct);',
                         'using var entryFrame = await CaptureGameWindowAsync(ct);',
                         'if (!found.Found) continue;',
                         'if (!await VerifyStableAbyssEntryReadyAsync(ct))',
                         '_input.TapScanCode(0x39, ct);',
                         '_abyssEntrySpaceCount = 1;',
                         '_abyssEntryLastSpaceAt = Environment.TickCount64;'):
            self.assertIn(required,block)
        self.assertLess(block.index('VerifyStableAbyssEntryReadyAsync(ct)'),block.index('_input.TapScanCode(0x39, ct);'))
        self.assertNotIn('_input.ClickClientPoint(_hwnd, found.Center);',block.split('else\n',1)[0])

    def test_bounded_retry_and_no_combat_until_verified(self):
        s=read("source/FishingAutomation/dungeon/ScenarioEngine.AbyssTimeout.cs")
        self.assertIn('private const int MaxAbyssEntrySpacePresses = 3;',s)
        entry=s.split('private async Task<DetectionResult> DetectAbyssEntryPanelAsync',1)[1].split('private async Task<bool> VerifyStableAbyssEntryReadyAsync',1)[0]
        self.assertIn('_detector.DetectAsync("abyss_enter", frame, ct)',entry)
        self.assertIn('FindCompactLabelAsync(',entry)
        self.assertIn('AbyssEntryButtonSafeArea, "입장하기"',entry)
        stable=s.split('private async Task<bool> VerifyStableAbyssEntryReadyAsync',1)[1].split('private async Task StartAbyssCombatClockAsync',1)[0]
        self.assertIn('Task.Delay(1200, ct)',stable)
        self.assertEqual(stable.count('CaptureGameWindowAsync(ct)'),2)
        clock=s.split('private async Task StartAbyssCombatClockAsync',1)[1].split('private async Task<bool> HandleAbyssLateClearAsync',1)[0]
        self.assertIn('if (entry.Found)',clock)
        self.assertIn('entryFrames >= 2',clock)
        self.assertIn('>= 2200',clock)
        self.assertIn('if (_abyssEntrySpaceCount >= MaxAbyssEntrySpacePresses)',clock)
        self.assertIn('if (await VerifyStableAbyssEntryReadyAsync(ct))',clock)
        self.assertIn('_input.TapScanCode(0x39, ct);',clock)
        self.assertIn('await HasAbyssCombatEvidenceAsync(frame, ct)',clock)
        self.assertIn('if (++gone >= 3)',clock)
        self.assertIn('abyss_dungeon_hallucination_anchorage',clock)
        self.assertLess(clock.index('if (entry.Found)'),clock.index('await HasAbyssCombatEvidenceAsync(frame, ct)'))
        self.assertIn('실제 던전 내부 3프레임',s)
        self.assertIn('DetectAbyssEntryPanelAsync(frame, ct)).Found',s)

    def test_only_first_abyss_entry_modified(self):
        s=read("source/FishingAutomation/dungeon/ScenarioEngine.cs")
        self.assertIn('if (step.Target.Equals("enter_bottom", StringComparison.OrdinalIgnoreCase))',s)
        self.assertIn('_input.TapScanCode(0x39);',s)
        self.assertIn('_input.ClickClientPoint(_hwnd, found.Center);',s)
        old=read("tests/release/test_v3314_abyss_space_entry.py")
        self.assertIn("class AbyssInitialEntrySpaceRegression",old)

if __name__=="__main__":
    unittest.main()
