"""Shared production reader wiring; behavioral seams run in retry-flow on Windows."""
import json
import unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
def read(p): return (ROOT / p).read_text(encoding='utf-8')
D = 'source/FishingAutomation/dungeon/'

class SharedClearEvidence(unittest.TestCase):
    def test_initial_and_post_click_paths_use_same_reader(self):
        engine = read(D+'ScenarioEngine.cs')
        for method,following in (
            ('DetectAbyssConfirmedClearAsync','DetectAbyssClearVisualAsync'),
            ('WaitForAbyssTouchPromptAsync','WaitForAbyssClearScreenGoneAsync'),
            ('WaitForAbyssClearScreenGoneAsync','AdvanceAbyssClearScreenAsync')):
            # Restrict to declaration, never an earlier call site.
            import re
            match=re.search(r'private async Task[^\n]* '+method+r'\(',engine)
            body=engine[match.start():]
            body=body[:body.index('private async Task',1)]
            self.assertIn('ReadAbyssClearEvidenceAsync(frame, ct)',body)
            self.assertIn('AbyssClearEvidencePolicy.',body)
            self.assertNotIn('HasAbyssActiveCombatHudAsync',body)
        network=read(D+'ScenarioEngine.NetworkRecovery.cs')
        hud=network.split('private async Task<bool> HasAbyssActiveCombatHudAsync(',1)[1].split('private async Task<bool> HasAbyssCombatEvidenceAsync(',1)[0]
        self.assertIn('ReadAbyssClearEvidenceAsync(frame, ct)',hud)
        self.assertIn('return !clearOverlay',hud)

    def test_bounds_and_no_input_in_shared_reader(self):
        shared=read(D+'ScenarioEngine.AbyssClearEvidence.cs')
        for text in ('"던전 클리어", ct','"화면을 터치해 주세요", ct',
                     'clearTitleArea.Contains(ocrTitle.Bounds)',
                     'clearTouchArea.Contains(ocrTouch.Bounds)',
                     'clearTitle = DetectionResult.NotFound',
                     'touch = DetectionResult.NotFound'):
            self.assertIn(text,shared)
        self.assertNotIn('_input.',shared)
        self.assertNotIn('AbyssTransitionTo(',shared)
        self.assertIn('ReadAbyssClearEvidenceAsync(frame, ct)',read(D+'ScenarioEngine.AbyssCompletedResult.cs'))
        retry=read(D+'ScenarioEngine.AbyssRetry.cs')
        self.assertIn('ReadAbyssClearEvidenceAsync(firstFrame, ct)',retry)
        self.assertIn('ReadAbyssClearEvidenceAsync(secondFrame, ct)',retry)

    def test_release_requires_behavioral_and_main_ci_checks(self):
        auth=json.loads(read('source/V3.3.24_RELEASE_AUTHORIZATION.json'))
        self.assertFalse(auth['realGameTested'])
        self.assertTrue(auth['mainCiRequired'])
        for path in ('.github/workflows/ci.yml','.github/workflows/release-v3324-auto-update.yml'):
            workflow=read(path)
            self.assertIn('test_v3324_shared_clear_evidence.py',workflow)
            self.assertIn('tests/retry-flow/Regression.csproj',workflow)
        self.assertIn('OCR-only clear pair survives missing image templates',read('tests/retry-flow/Program.cs'))

if __name__=='__main__': unittest.main()
