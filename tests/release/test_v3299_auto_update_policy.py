"""V3.2.99 recent-search Enter focus and unfiltered medicine click blocker."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-v3299-auto-update.yml"

class V3299RecentSearchPolicy(unittest.TestCase):
    def test_version_and_authorized_release(self):
        a = json.loads((ROOT / "source/V3.2.99_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(a["schema"], 1)
        self.assertEqual(a["version"], "V3.2.99")
        self.assertEqual(a["purpose"], "enter_focused_recent_search_medicine_crafting")
        self.assertEqual(a["sourcePullRequest"], 397)
        self.assertEqual(a["sourcePullRequests"], [397])
        self.assertIn("확인 했어", a["request"])
        self.assertFalse(a["realGameTested"])
        self.assertTrue(all(a[x] for x in ("mainCiRequired", "offlineRegressionRequired",
                                             "rollbackProvided", "postUpdateGameTestingPending")))
        # Historical V3.2.99 policy continues to guard the Enter-search flow
        # after a later version is built; never lock CI to a retired binary.
        current=(ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertTrue(any(f'CurrentVersion = "{v}"' in current for v in ("V3.2.99", "V3.3.00", "V3.3.01", "V3.3.02", "V3.3.03", "V3.3.04", "V3.3.05")))
        proj=(ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for version,assembly in (("3.2.99","3.2.99.0"),("3.3.0","3.3.0.0"),("3.3.1","3.3.1.0"),("3.3.2","3.3.2.0"),("3.3.3","3.3.3.0"),("3.3.4","3.3.4.0"),("3.3.5","3.3.5.0")):
            if f"<Version>{version}</Version>" in proj:
                self.assertIn(f"<AssemblyVersion>{assembly}</AssemblyVersion>", proj)
                self.assertIn(f"<FileVersion>{assembly}</FileVersion>", proj)
                break
        else:
            self.fail("unexpected app version; historical release regression refuses it")

    def test_medicine_and_food_enter_focus_no_stale_click(self):
        med=(ROOT / "source/FishingAutomation/dungeon/AlteringScreen.cs").read_text(encoding="utf-8")
        food=(ROOT / "source/FishingAutomation/dungeon/CraftingScreen.cs").read_text(encoding="utf-8")
        m=med.split("private async Task<bool> TrySelectMedicineRecipeBySearchAsync(")[1].split("private async Task SelectRecipeAsync(")[0]
        f=food.split("private async Task SearchProductAsync(")[1].split("private async Task<bool> IsProductDetailStructureAsync(")[0]
        for code in (m,f):
            self.assertIn("_ui.TapFresh(0x1C, ct); // Enter: focus", code)
            self.assertTrue("_ui.PasteFresh(" in code or "_ui.ReplaceTextFresh(" in code)
            self.assertIn("_ui.TapFresh(0x39, ct)", code)
        self.assertNotIn("ClickFresh(AlteringRecipeLayout.ProcessingSearchInputPoint", m)
        self.assertNotIn("ClickFresh(CraftingHubLayout.ProductSearchInputPoint", f)
        self.assertIn("_ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchFirstResultPoint", m)
        self.assertIn("_ui.ClickFresh(CraftingHubLayout.ProductFirstResultPoint", f)
        for marker in ("HasSingleMedicineSearchResultVisual(", "IsMedicineSearchApplyPopupVisible(",
                       "if (!firstOnly || popupVisible)", "if (!secondFirstOnly || secondPopupVisible)",
                       "await Task.Delay(180, ct)", "bool firstOnly", "bool secondFirstOnly"):
            self.assertIn(marker,m)
        self.assertNotIn("bool exactUnique",m)
        self.assertIn("2026-11-07 incident",med)
        self.assertIn("VerifyTwoFreshObservationsWithRetriesAsync",med)
        layout=(ROOT / "source/FishingAutomation/AlteringRecipeLayout.cs").read_text(encoding="utf-8")
        craftlayout=(ROOT / "source/FishingAutomation/CraftingHubLayout.cs").read_text(encoding="utf-8")
        self.assertIn("ProcessingSearchFirstResultPoint = new(218, 412)",layout)
        self.assertIn("ProductFirstResultPoint",craftlayout)
        self.assertIn("new(218, 613)",craftlayout)

    def test_public_release_gates(self):
        wf=WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("      - '.github/workflows/release-v3299-auto-update.yml'",wf)
        self.assertIn("  workflow_dispatch:",wf)
        self.assertNotIn("  workflow_run:",wf)
        for marker in ("Require V3.2.99 Enter focus",
                       "Require V3.2.99 F07 fresh-pair",
                       "V3.2.97 managed receipt blocks stale",
                       "Main production CI did not pass; refuse release",
                       "No successful production CI for current main SHA",
                       "main changed since CI; abort release",
                       "Smoke test executable",
                       "Get-FileHash $zip -Algorithm SHA256",
                       "ROLLBACK_PREVIOUS.cmd",
                       "tools/RollbackPrevious.ps1",
                       "MabiAuto_V3.2.99_Windows_Lite.zip.sha256",
                       "MabiAuto_V3.2.99_Source.zip",
                       "gh release create V3.2.99",
                       "gh release edit V3.2.99 --draft=false",
                       "Published release missing asset"):
            self.assertIn(marker,wf)
        self.assertLess(wf.index("Smoke test executable"),wf.index("Publish public auto-update Release"))
        ci=(ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("test_v3299_auto_update_policy.py",ci)
        self.assertIn("2026-11-07",(ROOT / "source/RELEASE_NOTES_V3.2.99.md").read_text(encoding="utf-8"))

if __name__=="__main__":
    unittest.main()
