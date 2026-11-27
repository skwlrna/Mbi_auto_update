"""V3.3.01: preserve verified medicine selection while removing redundant OCR,
and require 2+latest visual frames before food/item crafting Space."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def read(path):
    return (ROOT / path).read_text(encoding="utf-8")

class V3301ProductionReleaseSafety(unittest.TestCase):
    def test_version_and_real_game_state_are_explicit(self):
        current = read("source/FishingAutomation/UpdateManager.cs")
        project = read("source/FishingAutomation/FishingAutomation.csproj")
        if 'CurrentVersion = "V3.3.49"' in current:
            expected = ("3.3.49", "3.3.49.0", "3.3.49.0")
            workflow = read(".github/workflows/release-v3349-auto-update.yml")
            for marker in ("gh release create V3.3.49",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.48"' in current:
            expected = ("3.3.48", "3.3.48.0", "3.3.48.0")
            workflow = read(".github/workflows/release-v3348-auto-update.yml")
            for marker in ("gh release create V3.3.48",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.47"' in current:
            expected = ("3.3.47", "3.3.47.0", "3.3.47.0")
            workflow = read(".github/workflows/release-v3347-auto-update.yml")
            for marker in ("gh release create V3.3.47",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.46"' in current:
            expected = ("3.3.46", "3.3.46.0", "3.3.46.0")
            workflow = read(".github/workflows/release-v3346-auto-update.yml")
            for marker in ("gh release create V3.3.46",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.45"' in current:
            expected = ("3.3.45", "3.3.45.0", "3.3.45.0")
            workflow = read(".github/workflows/release-v3345-auto-update.yml")
            for marker in ("gh release create V3.3.45",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.44"' in current:
            expected = ("3.3.44", "3.3.44.0", "3.3.44.0")
            workflow = read(".github/workflows/release-v3344-auto-update.yml")
            for marker in ("gh release create V3.3.44",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.43"' in current:
            expected = ("3.3.43", "3.3.43.0", "3.3.43.0")
            workflow = read(".github/workflows/release-v3343-auto-update.yml")
            for marker in ("gh release create V3.3.43",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.42"' in current:
            expected = ("3.3.42", "3.3.42.0", "3.3.42.0")
            workflow = read(".github/workflows/release-v3342-auto-update.yml")
            for marker in ("gh release create V3.3.42",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.41"' in current:
            expected = ("3.3.41", "3.3.41.0", "3.3.41.0")
            workflow = read(".github/workflows/release-v3341-auto-update.yml")
            for marker in ("gh release create V3.3.41",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.40"' in current:
            expected = ("3.3.40", "3.3.40.0", "3.3.40.0")
            workflow = read(".github/workflows/release-v3340-auto-update.yml")
            for marker in ("gh release create V3.3.40",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.39"' in current:
            expected = ("3.3.39", "3.3.39.0", "3.3.39.0")
            workflow = read(".github/workflows/release-v3339-auto-update.yml")
            for marker in ("gh release create V3.3.39",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.38"' in current:
            expected = ("3.3.38", "3.3.38.0", "3.3.38.0")
            workflow = read(".github/workflows/release-v3338-auto-update.yml")
            for marker in ("gh release create V3.3.38",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.37"' in current:
            expected = ("3.3.37", "3.3.37.0", "3.3.37.0")
            workflow = read(".github/workflows/release-v3337-auto-update.yml")
            for marker in ("gh release create V3.3.37",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.36"' in current:
            expected = ("3.3.36", "3.3.36.0", "3.3.36.0")
            workflow = read(".github/workflows/release-v3336-auto-update.yml")
            for marker in ("gh release create V3.3.36",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.35"' in current:
            expected = ("3.3.35", "3.3.35.0", "3.3.35.0")
            workflow = read(".github/workflows/release-v3335-auto-update.yml")
            for marker in ("gh release create V3.3.35",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.34"' in current:
            expected = ("3.3.34", "3.3.34.0", "3.3.34.0")
            workflow = read(".github/workflows/release-v3334-auto-update.yml")
            for marker in ("gh release create V3.3.34",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.33"' in current:
            expected = ("3.3.33", "3.3.33.0", "3.3.33.0")
            workflow = read(".github/workflows/release-v3333-auto-update.yml")
            for marker in ("gh release create V3.3.33",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.32"' in current:
            expected = ("3.3.32", "3.3.32.0", "3.3.32.0")
            workflow = read(".github/workflows/release-v3332-auto-update.yml")
            for marker in ("gh release create V3.3.32",
                           "tests/altering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.31"' in current:
            expected = ("3.3.31", "3.3.31.0", "3.3.31.0")
            workflow = read(".github/workflows/release-v3331-auto-update.yml")
            for marker in ("gh release create V3.3.31",
                           "V3.3.31 21:32 verified K-first-card recovery regression",
                           "tests/altering-screen-integration/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.30"' in current:
            expected = ("3.3.30", "3.3.30.0", "3.3.30.0")
            workflow = read(".github/workflows/release-v3330-auto-update.yml")
            for marker in ("gh release create V3.3.30",
                           "V3.3.30 five-item standalone total-stock gathering regression",
                           "tests/gathering/Regression.csproj",
                           "No successful production CI for current main SHA"):
                self.assertIn(marker, workflow)
        elif 'CurrentVersion = "V3.3.01"' in current:
            expected = ("3.3.1", "3.3.1.0", "3.3.1.0")
        elif 'CurrentVersion = "V3.3.29"' in current:
            expected = ("3.3.29", "3.3.29.0", "3.3.29.0")
            workflow = read(".github/workflows/release-v3329-auto-update.yml")
            self.assertIn("gh release create V3.3.29", workflow)
            self.assertIn("V3.3.29 three-work leather mismatch independent F9 regression", workflow)
            self.assertIn("tests/altering/Regression.csproj", workflow)
        elif 'CurrentVersion = "V3.3.28"' in current:
            expected = ("3.3.28", "3.3.28.0", "3.3.28.0")
            workflow = read(".github/workflows/release-v3328-auto-update.yml")
            self.assertIn("gh release create V3.3.28", workflow)
            self.assertIn("test_v3328_clear_title_fallback.py", workflow)
            self.assertIn("tests/retry-flow/Regression.csproj", workflow)
        elif 'CurrentVersion = "V3.3.27"' in current:
            expected = ("3.3.27", "3.3.27.0", "3.3.27.0")
            workflow = read(".github/workflows/release-v3327-auto-update.yml")
            self.assertIn("gh release create V3.3.27", workflow)
            self.assertIn("tests/retry-flow/Regression.csproj", workflow)
        elif 'CurrentVersion = "V3.3.26"' in current:
            expected = ("3.3.26", "3.3.26.0", "3.3.26.0")
            workflow = read(".github/workflows/release-v3326-auto-update.yml")
            self.assertIn("gh release create V3.3.26", workflow)
            self.assertIn("tests/retry-flow/Regression.csproj", workflow)
        elif 'CurrentVersion = "V3.3.25"' in current:
            expected = ("3.3.25", "3.3.25.0", "3.3.25.0")
            workflow = read(".github/workflows/release-v3325-auto-update.yml")
            self.assertIn("gh release create V3.3.25", workflow)
            self.assertIn("test_v3325_confirmed_result_visual_retry.py", workflow)
        elif 'CurrentVersion = "V3.3.24"' in current:
            expected = ("3.3.24", "3.3.24.0", "3.3.24.0")
            workflow = read(".github/workflows/release-v3324-auto-update.yml")
            self.assertIn("gh release create V3.3.24", workflow)
            self.assertIn("test_v3324_shared_clear_evidence.py", workflow)
        elif 'CurrentVersion = "V3.3.23"' in current:
            expected = ("3.3.23", "3.3.23.0", "3.3.23.0")
            workflow = read(".github/workflows/release-v3323-auto-update.yml")
            self.assertIn("gh release create V3.3.23", workflow)
            self.assertIn("test_v3323_result_reward_timeout.py", workflow)
        elif 'CurrentVersion = "V3.3.22"' in current:
            expected = ("3.3.22", "3.3.22.0", "3.3.22.0")
            workflow = read(".github/workflows/release-v3322-auto-update.yml")
            self.assertIn("gh release create V3.3.22", workflow)
            self.assertIn("test_v3322_combat_result_rescue.py", workflow)
        elif 'CurrentVersion = "V3.3.21"' in current:
            expected = ("3.3.21", "3.3.21.0", "3.3.21.0")
            workflow = read(".github/workflows/release-v3321-auto-update.yml")
            self.assertIn("gh release create V3.3.21", workflow)
            self.assertIn("test_v3321_clear_overlay_veto.py", workflow)
        elif 'CurrentVersion = "V3.3.20"' in current:
            expected = ("3.3.20", "3.3.20.0", "3.3.20.0")
            workflow = read(".github/workflows/release-v3320-auto-update.yml")
            self.assertIn("gh release create V3.3.20", workflow)
            self.assertIn("test_v3320_combat_result_veto.py", workflow)
        elif 'CurrentVersion = "V3.3.19"' in current:
            expected = ("3.3.19", "3.3.19.0", "3.3.19.0")
            workflow = read(".github/workflows/release-v3319-auto-update.yml")
            self.assertIn("gh release create V3.3.19", workflow)
            self.assertIn("test_v3319_clear_touch_priority.py", workflow)
        elif 'CurrentVersion = "V3.3.18"' in current:
            expected = ("3.3.18", "3.3.18.0", "3.3.18.0")
            self.assertIn("gh release create V3.3.18",
                          read(".github/workflows/release-v3318-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.17"' in current:
            expected = ("3.3.17", "3.3.17.0", "3.3.17.0")
            self.assertIn("gh release create V3.3.17",
                          read(".github/workflows/release-v3317-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.16"' in current:
            expected = ("3.3.16", "3.3.16.0", "3.3.16.0")
            self.assertIn("gh release create V3.3.16",
                          read(".github/workflows/release-v3316-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.15"' in current:
            expected = ("3.3.15", "3.3.15.0", "3.3.15.0")
            self.assertIn("gh release create V3.3.15",
                          read(".github/workflows/release-v3315-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.14"' in current:
            expected = ("3.3.14", "3.3.14.0", "3.3.14.0")
            self.assertIn("gh release create V3.3.14",
                          read(".github/workflows/release-v3314-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.13"' in current:
            expected = ("3.3.13", "3.3.13.0", "3.3.13.0")
            self.assertIn("gh release create V3.3.13",
                          read(".github/workflows/release-v3313-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.12"' in current:
            expected = ("3.3.12", "3.3.12.0", "3.3.12.0")
            self.assertIn("gh release create V3.3.12",
                          read(".github/workflows/release-v3312-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.11"' in current:
            expected = ("3.3.11", "3.3.11.0", "3.3.11.0")
            self.assertIn("gh release create V3.3.11",
                          read(".github/workflows/release-v3311-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.10"' in current:
            expected = ("3.3.10", "3.3.10.0", "3.3.10.0")
            self.assertIn("gh release create V3.3.10",
                          read(".github/workflows/release-v3310-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.09"' in current:
            expected = ("3.3.9", "3.3.9.0", "3.3.9.0")
            self.assertIn("gh release create V3.3.09",
                          read(".github/workflows/release-v3309-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.08"' in current:
            expected = ("3.3.8", "3.3.8.0", "3.3.8.0")
            self.assertIn("gh release create V3.3.08",
                          read(".github/workflows/release-v3308-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.07"' in current:
            expected = ("3.3.7", "3.3.7.0", "3.3.7.0")
            self.assertIn("gh release create V3.3.07",
                          read(".github/workflows/release-v3307-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.06"' in current:
            expected = ("3.3.6", "3.3.6.0", "3.3.6.0")
            self.assertIn("gh release create V3.3.06",
                          read(".github/workflows/release-v3306-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.05"' in current:
            expected = ("3.3.5", "3.3.5.0", "3.3.5.0")
            self.assertIn("gh release create V3.3.05",
                          read(".github/workflows/release-v3305-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.04"' in current:
            expected = ("3.3.4", "3.3.4.0", "3.3.4.0")
            self.assertIn("gh release create V3.3.04",
                          read(".github/workflows/release-v3304-auto-update.yml"))
        elif 'CurrentVersion = "V3.3.03"' in current:
            expected = ("3.3.3", "3.3.3.0", "3.3.3.0")
            self.assertIn("gh release create V3.3.03",
                          read(".github/workflows/release-v3303-auto-update.yml"))
        else:
            self.assertIn('CurrentVersion = "V3.3.02"', current)
            expected = ("3.3.2", "3.3.2.0", "3.3.2.0")
            self.assertIn("gh release create V3.3.02",
                          read(".github/workflows/release-v3302-auto-update.yml"))
        for key, value in zip(("Version","AssemblyVersion","FileVersion"), expected):
            self.assertIn(f"<{key}>{value}</{key}>", project)
        auth = json.loads(read("source/V3.3.01_RELEASE_AUTHORIZATION.json"))
        self.assertEqual(auth["version"], "V3.3.01")
        self.assertEqual(auth["purpose"],
                         "medicine_consecutive_filter_reuse_and_replace_text")
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

    def test_consecutive_filter_reuse_and_replacement(self):
        code = read("source/FishingAutomation/dungeon/AlteringScreen.cs")
        search = code.split("private async Task<bool> TrySelectMedicineRecipeBySearchAsync")[1].split("private async Task SelectRecipeAsync")[0]
        reuse = search.split('if (string.Equals(_medicineFilterKey')[1].split('_medicineFilterKey = null;\n        await RequireManagedIdleAsync')[0]
        self.assertIn("ClickVerifiedFreshAsync", reuse)
        self.assertIn("HasSingleMedicineSearchResultVisual", reuse)
        self.assertIn("return true;", reuse)
        self.assertNotIn("ProcessingSearchIconPoint", reuse)
        self.assertNotIn("ReplaceTextFresh", reuse)
        self.assertIn("_ui.ReplaceTextFresh(plan.DisplayName, ct)", search)
        self.assertIn("CanReuseMedicineFilter(directive, _medicineFilterKey, RecipeCacheKey(plan))", code)
        runtime = read("source/FishingAutomation/dungeon/ProductionUiRuntime.cs")
        self.assertIn("_input.ReplaceText(text, ct)", runtime)

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
            self.assertIn("_ui.ClickFresh(CraftingHubLayout.CraftGoButtonPoint, ct)", part)
            self.assertNotIn('"제작하기",', part)
            self.assertIn("InventoryOnlyCountAsync", part)
            self.assertIn("WaitForCompletionAsync", part)
        self.assertIn("new(218, 613)", read("source/FishingAutomation/CraftingHubLayout.cs"))
        self.assertIn("new(218, 412)", read("source/FishingAutomation/AlteringRecipeLayout.cs"))

    def test_ci_gated_one_shot_public_release_and_rollback(self):
        wf = read(".github/workflows/release-v3301-auto-update.yml")
        for marker in ("      - '.github/workflows/release-v3301-auto-update.yml'",
                       "  workflow_dispatch:",
                       "Require user-approved V3.3.01",
                       "Require V3.3.01 medicine F07 selection evidence",
                       "Require V3.3.01 food and item craft-ready",
                       "main changed since CI; abort release",
                       "Main production CI did not pass; refuse release",
                       "Smoke test executable",
                       "Get-FileHash $zip -Algorithm SHA256",
                       "RollbackPrevious.ps1",
                       "MabiAuto_V3.3.01_Windows_Lite.zip.sha256",
                       "MabiAuto_V3.3.01_Source.zip",
                       "gh release create V3.3.01",
                       "gh release edit V3.3.01 --draft=false",
                       "Published release missing asset"):
            self.assertIn(marker, wf)
        self.assertNotIn("  workflow_run:", wf)
        self.assertLess(wf.index("Smoke test executable"),
                        wf.index("Publish public auto-update Release"))
        self.assertIn("test_v3301_auto_update_policy.py", read(".github/workflows/ci.yml"))
        self.assertIn("2026-11-07", read("source/RELEASE_NOTES_V3.3.01.md"))

if __name__ == "__main__":
    unittest.main()
