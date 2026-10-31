"""Release gates for the user's rim-free F9 update; offline is not live proof."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

class F9ShortcutReleasePolicy(unittest.TestCase):
    def test_authorization_and_matching_binary_versions(self):
        auth = json.loads((ROOT / 'source/V3.2.77_RELEASE_AUTHORIZATION.json').read_text(encoding="utf-8"))
        self.assertEqual(auth['version'], 'V3.2.77')
        self.assertEqual(auth['purpose'], 'f9_shortcut_field_without_minimap')
        self.assertEqual(auth['sourcePullRequest'], 344)
        self.assertEqual(auth['request'], '진행해')
        for flag in ['rollbackProvided', 'mainCiRequired', 'postUpdateGameTestingPending']:
            self.assertIs(auth[flag], True)
        self.assertIs(auth['realGameTested'], False)
        self.assertIn('CurrentVersion = "V3.2.77"', (ROOT / 'source/FishingAutomation/UpdateManager.cs').read_text(encoding="utf-8"))
        project = (ROOT / 'source/FishingAutomation/FishingAutomation.csproj').read_text(encoding="utf-8")
        for tag, version in [('Version', '3.2.77'), ('AssemblyVersion', '3.2.77.0'), ('FileVersion', '3.2.77.0')]:
            self.assertIn(f'<{tag}>{version}</{tag}>', project)

    def test_exact_main_ci_packaging_and_publication_gates(self):
        workflow = (ROOT / '.github/workflows/release-v3277-auto-update.yml').read_text(encoding="utf-8")
        for gate in ['workflow_run.conclusion == \'success\'', 'workflow_run.event == \'push\'',
                     'workflow_run.head_branch == \'main\'', 'No successful production CI for current main SHA',
                     'main changed since CI; abort release', 'V3.2.77 tag already exists',
                     'Smoke test executable', "VersionInfo.FileVersion -ne '3.2.77.0'",
                     'BUILD_COMMIT.txt', 'Get-FileHash $zip -Algorithm SHA256',
                     'MabiAuto_V3.2.77_Windows_Lite.zip.sha256', 'MabiAuto_V3.2.77_Source.zip',
                     'ROLLBACK_PREVIOUS.cmd', 'tools/RollbackPrevious.ps1', '--draft --verify-tag',
                     'Published release missing asset', "$a.sourcePullRequest -ne 344"]:
            self.assertIn(gate, workflow)
        for key in ['k', 'i']:
            self.assertIn(f'release/abyss/templates/managed_{key}_key.png', workflow)
            self.assertTrue((ROOT / f'source/FishingAutomation/abyss/templates/managed_{key}_key.png').exists())
        self.assertNotIn('3.2.76', workflow)
        self.assertIn('test_v3277_auto_update_policy.py', (ROOT / '.github/workflows/ci.yml').read_text(encoding="utf-8"))

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
