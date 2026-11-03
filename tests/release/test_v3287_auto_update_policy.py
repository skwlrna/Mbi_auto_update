"""V3.2.87 one-shot official release gates; tests never run a game or publish."""
import json
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RELEASE = ROOT / ".github/workflows/release-v3287-auto-update.yml"
SCREEN = ROOT / "source/FishingAutomation/dungeon/AlteringScreen.cs"


class KISpaceSingleFieldReleasePolicy(unittest.TestCase):
    def test_explicit_user_authorization_and_exact_binary_versions(self):
        a = json.loads((ROOT / "source/V3.2.87_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(a["schema"], 1)
        self.assertEqual(a["version"], "V3.2.87")
        self.assertEqual(a["purpose"], "f9_ki_space_single_field_release")
        self.assertEqual(a["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(a["request"], "진행해")
        self.assertEqual(a["sourcePullRequest"], 371)
        self.assertEqual(a["sourcePullRequests"], [371])
        for key in ("mainCiRequired", "offlineRegressionPassed", "rollbackProvided",
                    "postUpdateGameTestingPending"):
            self.assertIs(a[key], True)
        self.assertIs(a["realGameTested"], False)
        version = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.87"', version)
        project = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for key, value in (("Version", "3.2.87"), ("AssemblyVersion", "3.2.87.0"),
                           ("FileVersion", "3.2.87.0")):
            self.assertIn(f"<{key}>{value}</{key}>", project)

    def test_one_shot_publisher_checks_exact_main_and_assets(self):
        w = RELEASE.read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]\n    paths:", w)
        self.assertIn("- '.github/workflows/release-v3287-auto-update.yml'", w)
        self.assertIn("  workflow_dispatch:", w)
        self.assertNotIn("  workflow_run:", w)
        for token in (
            "github.ref == 'refs/heads/main'",
            "No successful production CI for current main SHA",
            "Main production CI did not pass; refuse release",
            "main changed since CI; abort release",
            "V3.2.87 tag already exists",
            "Tag exists without release; manual recovery required",
            "Smoke test executable",
            "3.2.87.0",
            "MabiAuto_V3.2.87_Windows_Lite.zip",
            "MabiAuto_V3.2.87_Windows_Lite.zip.sha256",
            "MabiAuto_V3.2.87_Source.zip",
            "BUILD_COMMIT.txt",
            "Get-FileHash $zip -Algorithm SHA256",
            "ROLLBACK_PREVIOUS.cmd",
            "tools/RollbackPrevious.ps1",
            "gh release create V3.2.87",
            "--draft --verify-tag",
            "Published release missing asset",
            "$a.sourcePullRequest -ne 371",
            "CanAuthorizeManagedKSpaceFallback",
        ):
            self.assertIn(token, w)
        self.assertIn("Require compiled production screen regressions before packaging", w)
        self.assertIn("test_v3287_auto_update_policy.py", w)
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.87.md").exists())

    def test_field_authorization_excludes_legacy_templates(self):
        s = SCREEN.read_text(encoding="utf-8")
        m = re.search(r"Func<Bitmap, Task<bool>>\? verifyManagedK = null;(.*?)await RequireManagedIdleAsync\(plan, directive, \"가공 메뉴 K 직전\"", s, re.S)
        self.assertIsNotNone(m)
        entry = m.group(1)
        self.assertIn("HasManagedKISpaceFieldHudAsync", entry)
        self.assertIn("CanAuthorizeManagedKSpaceFallback", entry)
        self.assertIn("Task.Delay(180, ct)", entry)
        for old in ("DetectManagedWorldHudTemplates", "CanAuthorizeManagedKEntry",
                    "CanAuthorizeManagedKFieldFallback",
                    "CanAuthorizeManagedKHomeEndFieldFallback", "FindManagedWorldHudKeysAsync"):
            self.assertNotIn(old, entry)
        scene = re.search(r"private async Task<ManagedProductionSceneKind> ClassifyManagedProductionSceneAsync(.*?)private async Task EnterFacilityAsync", s, re.S)
        final = re.search(r"private async Task<bool> VerifyManagedKFrameAsync(.*?)internal static bool HasManagedFoodCraftingNavigationVisual", s, re.S)
        self.assertIsNotNone(scene)
        self.assertIsNotNone(final)
        self.assertIn("HasManagedKISpaceFieldHudAsync", scene.group(1))
        self.assertIn("ClassifyManagedProductionSceneAsync", final.group(1))
        for old in ("DetectManagedWorldHudTemplates", "DetectManagedWorldHudKeys",
                    "HasManagedKEntryFieldHudVisual"):
            self.assertNotIn(old, scene.group(1))
            self.assertNotIn(old, final.group(1))
        self.assertIn("RequireManagedIdleAsync", s)
        self.assertIn("HasManagedKEntryBlockingModal", s)
        self.assertIn("TapVerifiedFreshAsync", s)

    def test_registered_in_main_ci(self):
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("test_v3287_auto_update_policy.py", ci)


if __name__ == "__main__":
    unittest.main()
