"""V3.3.00: preserve verified medicine selection while removing redundant OCR,
and require 2+latest visual frames before food/item crafting Space."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(path):
    return (ROOT / path).read_text(encoding="utf-8")

class V3300ProductionReleaseSafety(unittest.TestCase):
    def test_version_and_real_game_state_are_explicit(self):
        self.assertTrue(any(f'CurrentVersion = "{v}"' in read("source/FishingAutomation/UpdateManager.cs") for v in ("V3.3.00", "V3.3.01")))
        project = read("source/FishingAutomation/FishingAutomation.csproj")
        for key, value in (("Version","3.3.1"), ("AssemblyVersion","3.3.1.0"),
                           ("FileVersion","3.3.1.0")):
            self.assertIn(f"<{key}>{value}</{key}>", project)
        auth = json.loads(read("source/V3.3.00_RELEASE_AUTHORIZATION.json"))
        self.assertEqual(auth["version"], "V3.3.00")
        self.assertEqual(auth["purpose"],
                         "medicine_verified_search_no_duplicate_title_ocr_and_crafting_visual_ready")
        self.assertIn("수정하고 배포해", auth["request"])
        self.assertGreater(auth["sourcePullRequest"], 0)
        self.assertFalse(auth["realGameTested"])
        for key in ("rollbackProvided","mainCiRequired","offlineRegressionRequired",
                    "postUpdateGameTestingPending"):
            self.assertTrue(auth[key], key)

    def test_medicine_search_authorizes_same_recipe_without_later_name_ocr(self):
        code = read("source/FishingAutomation/dungeon/AlteringScreen.cs")
        finder = code.split("private async Task<DetectionResult?> FindRecipeAsync")[1].split(
            "private async Task<bool> IsRecipeDetailStructureAsync")[0]
        search = code.split("private async Task<bool> TrySelectMedicineRecipeBySearchAsync")[1].split(
            "private async Task SelectRecipeAsync")[0]
        queue = code.split("public async Task QueueAsync")[1].split(
            "public async Task<bool> CollectAsyncAtBoundary")[0]
        self.assertLess(finder.index("_selectedMedicineSearchKey"), finder.index(
            "var exact = await FindAsync"))
        self.assertIn("TryFindFreeProcessButtonVisual(frame, out _)", finder)
        detail = code.split("private async Task<bool> IsRecipeDetailStructureAsync")[1].split(
            "private static bool TryFindFreeProcessButtonVisual")[0]
        self.assertLess(detail.index("_selectedMedicineSearchKey is not null"),
                        detail.index('FindAsync(frame, new(100, 690, 580, 200), "필요한 재료"'))
        self.assertIn("string.Equals(_selectedMedicineSearchKey, RecipeCacheKey(plan)", finder)
        for x in ("bool firstOnly = HasSingleMedicineSearchResultVisual",
                  "bool secondFirstOnly = HasSingleMedicineSearchResultVisual",
                  "if (!secondFirstOnly || secondPopupVisible)",
                  "_ui.ClickFresh(AlteringRecipeLayout.ProcessingSearchFirstResultPoint, ct)",
                  "if (!await IsRecipeDetailStructureAsync(popup, ct))",
                  "_selectedMedicineSearchKey = RecipeCacheKey(plan)"):
            self.assertIn(x, search)
        self.assertLess(search.index("if (!await IsRecipeDetailStructureAsync(popup, ct))"),
                        search.rindex("_selectedMedicineSearchKey = RecipeCacheKey(plan)"))
        self.assertIn("_selectedMedicineSearchKey = null", queue)
        for x in ("VerifyTwoFreshObservationsWithRetriesAsync",
                  "DetectRemoteProcessStateAsync",
                  "IsStableFreshFreeActionTarget",
                  "ClickVerifiedFreshAsync",
                  "HandleOptionalMaterialTransferPromptAsync"):
            self.assertIn(x, queue)

    def test_food_and_item_station_ready_has_no_text_ocr_hard_gate(self):
        code = read("source/FishingAutomation/dungeon/CraftingScreen.cs")
        return_path = code.split("public async Task ReturnToStationAndCraftAsync")[1].split(
            "private async Task FinishDirectCraftAsync")[0]
        direct_path = code.split("private async Task FinishDirectCraftAsync")[1].split(
            "private DetectionResult? FindQuestTitleCandidate")[0]
        self.assertIn("private static bool HasCraftReadyActionVisual(Bitmap frame)", code)
        self.assertIn("CraftingHubLayout.CraftActionButtonArea", code)
        self.assertIn("private async Task<bool> HasTwoFreshCraftReadyFramesAsync(", code)
        self.assertIn("private bool HasCraftReadyWithStationTransition(Bitmap frame)", code)
        self.assertIn("_directCraftBeforeTravel", code)
        self.assertIn("ProductDetailPanelArea) < 0.15", code)
        for part in (return_path, direct_path):
            self.assertIn("HasTwoFreshCraftReadyFramesAsync(frame, ct)", part)
            self.assertIn("using var immediate = Capture(ct)", part)
            self.assertIn("if (!HasCraftReadyWithStationTransition(immediate))", part)
            self.assertIn("_ui.TapFresh(0x39, ct)", part)
            self.assertNotIn('"제작하기",', part)
            self.assertIn("InventoryOnlyCountAsync", part)
            self.assertIn("WaitForCompletionAsync", part)
        self.assertIn("new(218, 613)", read("source/FishingAutomation/CraftingHubLayout.cs"))
        self.assertIn("new(218, 412)", read("source/FishingAutomation/AlteringRecipeLayout.cs"))

    def test_ci_gated_one_shot_public_release_and_rollback(self):
        wf = read(".github/workflows/release-v3300-auto-update.yml")
        for marker in ("      - '.github/workflows/release-v3300-auto-update.yml'",
                       "  workflow_dispatch:",
                       "Require user-approved V3.3.00",
                       "Require V3.3.00 medicine F07 selection evidence",
                       "Require V3.3.00 food and item craft-ready",
                       "main changed since CI; abort release",
                       "Main production CI did not pass; refuse release",
                       "Smoke test executable",
                       "Get-FileHash $zip -Algorithm SHA256",
                       "RollbackPrevious.ps1",
                       "MabiAuto_V3.3.00_Windows_Lite.zip.sha256",
                       "MabiAuto_V3.3.00_Source.zip",
                       "gh release create V3.3.00",
                       "gh release edit V3.3.00 --draft=false",
                       "Published release missing asset"):
            self.assertIn(marker, wf)
        self.assertNotIn("  workflow_run:", wf)
        self.assertLess(wf.index("Smoke test executable"),
                        wf.index("Publish public auto-update Release"))
        self.assertIn("test_v3300_auto_update_policy.py", read(".github/workflows/ci.yml"))
        self.assertIn("2026-11-07", read("source/RELEASE_NOTES_V3.3.00.md"))

if __name__ == "__main__":
    unittest.main()
