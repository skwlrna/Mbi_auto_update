"""V3.3.00: medicine F07 repeated title OCR and crafting-ready visual gate."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

class V3300CraftingAndMedicinePolicy(unittest.TestCase):
    def test_version_authorization_and_rollback(self):
        a=json.loads((ROOT/"source/V3.3.00_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(a["version"],"V3.3.00")
        self.assertEqual(a["sourcePullRequest"],398)
        self.assertEqual(a["sourcePullRequests"],[398])
        self.assertEqual(a["purpose"],"medicine_f07_redundant_ocr_removal_and_crafting_ready_visual")
        self.assertIn("수정하고 배포해",a["request"])
        self.assertFalse(a["realGameTested"])
        for key in ("postUpdateGameTestingPending","offlineRegressionRequired","mainCiRequired","rollbackProvided"):
            self.assertTrue(a[key])
        self.assertIn('CurrentVersion = "V3.3.00"',(ROOT/"source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8"))
        p=(ROOT/"source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for tag,v in (("Version","3.3.0"),("AssemblyVersion","3.3.0.0"),("FileVersion","3.3.0.0")):
            self.assertIn(f"<{tag}>{v}</{tag}>",p)

    def test_medicine_selected_recipe_scoped_f07_without_title_ocr(self):
        code=(ROOT/"source/FishingAutomation/dungeon/AlteringScreen.cs").read_text(encoding="utf-8")
        search=code.split("private async Task<bool> TrySelectMedicineRecipeBySearchAsync(")[1].split("private async Task SelectRecipeAsync(")[0]
        select=code.split("private async Task SelectRecipeAsync(")[1].split("private async Task",1)[0]
        find=code.split("private async Task<DetectionResult?> FindRecipeAsync(")[1].split("private async Task<bool> IsRecipeDetailStructureAsync(")[0]
        self.assertIn("_selectedMedicineSearchRecipeKey = RecipeCacheKey(plan);",search)
        self.assertLess(search.index("IsRecipeDetailStructureAsync(popup"),search.index("_selectedMedicineSearchRecipeKey = RecipeCacheKey(plan)"))
        self.assertIn("_selectedMedicineSearchRecipeKey = null;",select)
        self.assertIn('string.Equals(_selectedMedicineSearchRecipeKey, RecipeCacheKey(plan), StringComparison.Ordinal)',find)
        self.assertLess(find.index("_selectedMedicineSearchRecipeKey"),find.index("var exact = await FindAsync"))
        self.assertIn("TryFindFreeProcessButtonVisual(frame, out _)",find)
        for marker in ("HasSingleMedicineSearchResultVisual","IsMedicineSearchApplyPopupVisible",
                       "VerifyTwoFreshObservationsWithRetriesAsync","DetectRemoteProcessStateAsync(fresh, token)",
                       "IsStableFreshFreeActionTarget","ClickVerifiedFreshAsync(liveCenter",
                       "await HandleOptionalMaterialTransferPromptAsync(ct)"):
            self.assertIn(marker,code)
        self.assertIn("ProcessingSearchFirstResultPoint = new(218, 412)",
                      (ROOT/"source/FishingAutomation/AlteringRecipeLayout.cs").read_text(encoding="utf-8"))

    def test_food_item_final_space_has_visual_state_proof(self):
        source=(ROOT/"source/FishingAutomation/dungeon/CraftingScreen.cs").read_text(encoding="utf-8")
        layout=(ROOT/"source/FishingAutomation/CraftingHubLayout.cs").read_text(encoding="utf-8")
        wait=source.split("public async Task ReturnToStationAndCraftAsync(")[1].split("private DetectionResult? FindQuestTitleCandidate(")[0]
        evidence=source.split("private async Task<bool> HasCraftReadyEvidenceAsync(")[1].split("public async Task ReturnToStationAndCraftAsync(")[0]
        self.assertIn("HasCraftReadyActionVisual(frame)",evidence)
        self.assertIn("MeasureVisualChangeRatio(",evidence)
        self.assertIn("HasConfirmedCraftReadyAsync(",evidence)
        self.assertIn("Task.Delay(180, ct)",evidence)
        self.assertIn("GetActivityAsync(ct)",evidence)
        self.assertIn("!activity.IsAutoTraveling",evidence)
        self.assertIn("HasConfirmedCraftReadyAsync(frame, ct)",wait)
        self.assertEqual(wait.count("TapVerifiedFreshAsync("),2)
        self.assertNotIn("_ui.TapFresh(0x39, ct)",wait)
        self.assertIn("_craftReadyPreTravel = Capture(ct);",source)
        self.assertIn("CraftActionButtonArea",layout)
        self.assertIn("HasCraftReadyActionVisual(Bitmap frame)",layout)
        self.assertIn("new(218, 613)",layout)
        self.assertIn("new(507, 950)",layout)

    def test_release_main_ci_gates(self):
        wf=(ROOT/".github/workflows/release-v3300-auto-update.yml").read_text(encoding="utf-8")
        for marker in ("'refs/heads/main'","release-v3300-auto-update.yml",
                       "Require V3.3.00 scoped F07 and crafting visual proof",
                       "No successful production CI for current main SHA",
                       "Main production CI did not pass; refuse release",
                       "Smoke test executable","MabiAuto_V3.3.00_Windows_Lite.zip.sha256",
                       "MabiAuto_V3.3.00_Source.zip",
                       "gh release create V3.3.00",
                       "gh release edit V3.3.00 --draft=false",
                       "main changed since CI; abort release",
                       "ROLLBACK_PREVIOUS.cmd"):
            self.assertIn(marker,wf)
        self.assertNotIn("  workflow_run:",wf)
        self.assertIn("test_v3300_auto_update_policy.py",
                      (ROOT/".github/workflows/ci.yml").read_text(encoding="utf-8"))
        self.assertIn("14:31", (ROOT/"source/RELEASE_NOTES_V3.3.00.md").read_text(encoding="utf-8"))

if __name__=="__main__":
    unittest.main()
