"""Release gates for the user's rim-free F9 update; offline is not live proof."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

class ReviewedFollowupsReleasePolicy(unittest.TestCase):
    def test_authorization_and_matching_binary_versions(self):
        auth = json.loads((ROOT / 'source/V3.2.85_RELEASE_AUTHORIZATION.json').read_text(encoding="utf-8"))
        self.assertEqual(auth['version'], 'V3.2.85')
        self.assertEqual(auth['purpose'], 'reviewed_followup_fixes')
        self.assertEqual(auth['sourcePullRequest'], 360)
        self.assertEqual(auth['sourcePullRequests'], [354, 358, 359, 360, 361, 363])
        self.assertEqual(auth['request'], '지금 최신 업데이트 이후 올라온 깃허브 pr 확인해보고 검증하고 배포해')
        for flag in ['rollbackProvided', 'mainCiRequired', 'postUpdateGameTestingPending']:
            self.assertIs(auth[flag], True)
        self.assertIs(auth['realGameTested'], False)
        current = (ROOT / 'source/FishingAutomation/UpdateManager.cs').read_text(encoding="utf-8")
        if 'CurrentVersion = "V3.2.85"' not in current:
            return  # Historical release; the current version has its own exact-version gate.
        self.assertIn('CurrentVersion = "V3.2.85"', (ROOT / 'source/FishingAutomation/UpdateManager.cs').read_text(encoding="utf-8"))
        project = (ROOT / 'source/FishingAutomation/FishingAutomation.csproj').read_text(encoding="utf-8")
        for tag, version in [('Version', '3.2.85'), ('AssemblyVersion', '3.2.85.0'), ('FileVersion', '3.2.85.0')]:
            self.assertIn(f'<{tag}>{version}</{tag}>', project)

    def test_exact_main_ci_packaging_and_publication_gates(self):
        workflow = (ROOT / '.github/workflows/release-v3285-auto-update.yml').read_text(encoding="utf-8")
        for gate in ['workflow_run.conclusion == \'success\'', 'workflow_run.event == \'push\'',
                     'workflow_run.head_branch == \'main\'', 'No successful production CI for current main SHA',
                     'main changed since CI; abort release', 'V3.2.85 tag already exists',
                     'Smoke test executable', "VersionInfo.FileVersion -ne '3.2.85.0'",
                     'BUILD_COMMIT.txt', 'Get-FileHash $zip -Algorithm SHA256',
                     'MabiAuto_V3.2.85_Windows_Lite.zip.sha256', 'MabiAuto_V3.2.85_Source.zip',
                     'ROLLBACK_PREVIOUS.cmd', 'tools/RollbackPrevious.ps1', '--draft --verify-tag',
                     'Published release missing asset', "$a.sourcePullRequest -ne 360"]:
            self.assertIn(gate, workflow)
        self.assertIn('Require compiled production screen regressions before packaging', workflow)
        self.assertIn('tests/daily-interruptions/Regression.csproj', workflow)
        self.assertIn('greeting-variable.png', workflow)
        for key in ['k', 'i']:
            self.assertIn(f'release/abyss/templates/managed_{key}_key.png', workflow)
            self.assertTrue((ROOT / f'source/FishingAutomation/abyss/templates/managed_{key}_key.png').exists())
        self.assertNotIn('3.2.76', workflow)
        self.assertIn('test_v3285_auto_update_policy.py', (ROOT / '.github/workflows/ci.yml').read_text(encoding="utf-8"))

    def test_rim_removal_and_incident_coverage(self):
        screen = (ROOT / 'source/FishingAutomation/dungeon/AlteringScreen.cs').read_text(encoding='utf-8-sig')
        for obsolete in ['HasManagedBlueMinimapRingVisual', 'HasManagedKEntryNonGoldFieldHudVisual', 'minimapGold']:
            self.assertNotIn(obsolete, screen)
        self.assertIn('WaitForManagedFacilityExitAsync', screen)
        self.assertIn('TimeSpan.FromSeconds(3)', screen)
        tests = (ROOT / 'tests/altering-screen-integration/Program.cs').read_text(encoding='utf-8-sig')
        for timestamp in ['022454', '070147']:
            name = f'field-workshop-20261031-{timestamp}.png'
            self.assertIn(name, tests)
            self.assertTrue((ROOT / 'tests/altering-screen-integration/managed-field-fixtures' / name).exists())
        self.assertIn('new Rectangle(640, 94, 160, 145)', tests)

if __name__ == '__main__':
    unittest.main()
