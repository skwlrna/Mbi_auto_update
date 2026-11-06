"""V3.2.95 single-bundle release gates. Source-only; never starts a game or publishes."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PUBLISHER = ROOT / ".github/workflows/release-v3295-auto-update.yml"


class V3295SingleReleaseSafety(unittest.TestCase):
    def test_exact_user_authorization_and_versions(self):
        auth = json.loads((ROOT / "source/V3.2.95_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(auth["schema"], 1)
        self.assertEqual(auth["version"], "V3.2.95")
        self.assertEqual(auth["purpose"], "medicine_first_recipe_card_coordinate_correction_release")
        self.assertEqual(auth["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(auth["request"], "배포해")
        self.assertEqual(auth["sourcePullRequest"], 389)
        self.assertEqual(auth["sourcePullRequests"], [389])
        for key in ("offlineRegressionRequired", "mainCiRequired", "rollbackProvided",
                    "postUpdateGameTestingPending"):
            self.assertIs(auth[key], True)
        self.assertIs(auth["realGameTested"], False)
        current = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        # Active V3.2.95 publisher must match the actual updater binary.
        self.assertIn('CurrentVersion = "V3.2.95"', current)
        csproj = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for field, value in (("Version", "3.2.95"), ("AssemblyVersion", "3.2.95.0"),
                             ("FileVersion", "3.2.95.0")):
            self.assertIn(f"<{field}>{value}</{field}>", csproj)

    def test_exact_main_ci_one_shot_and_rollbacks(self):
        w = PUBLISHER.read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]\n    paths:", w)
        self.assertIn("- '.github/workflows/release-v3295-auto-update.yml'", w)
        self.assertIn("  workflow_dispatch:", w)
        self.assertNotIn("  workflow_run:", w)
        for token in (
            "github.ref == 'refs/heads/main'",
            "No successful production CI for current main SHA",
            "Main production CI did not pass; refuse release",
            "main changed since CI; abort release",
            "V3.2.95 tag already exists",
            "Tag exists without release; manual recovery required",
            "Smoke test executable",
            "3.2.95.0",
            "MabiAuto_V3.2.95_Windows_Lite.zip",
            "MabiAuto_V3.2.95_Windows_Lite.zip.sha256",
            "MabiAuto_V3.2.95_Source.zip",
            "BUILD_COMMIT.txt",
            "Get-FileHash $zip -Algorithm SHA256",
            "ROLLBACK_PREVIOUS.cmd",
            "tools/RollbackPrevious.ps1",
            "gh release create V3.2.95",
            "--draft --verify-tag",
            "Published release missing asset",
            "$a.sourcePullRequest -ne 389",
            "Require V3.2.95 integrated restart and passive-watch regressions",
        ):
            self.assertIn(token, w)
        self.assertIn("test_v3295_auto_update_policy.py", w)
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.95.md").exists())

    def test_integrated_observation_and_replay_guards(self):
        for path in (
            "source/FishingAutomation/CraftingRestartSafetyJournal.cs",
            "source/FishingAutomation/dungeon/AbyssPassiveWaitAdvisory.cs",
            "source/FishingAutomation/MacroRuntimeStability.cs",
            "docs/CRAFTING_RESTART_SAFETY_JOURNAL.md",
            "docs/STABILITY_BUNDLE_V3288.md",
            "tests/stability-phase1/Program.cs",
            "tests/retry-flow/Regression.csproj",
            "tests/crafting/CraftingRestartSafetyTests.cs",
        ):
            self.assertTrue((ROOT / path).exists(), path)
        craft = (ROOT / "source/FishingAutomation/CraftingAutomation.cs").read_text(encoding="utf-8")
        self.assertIn("restartJournal?.BeginBatch(", craft)
        self.assertIn("restartJournal?.ConfirmBatch(", craft)
        form = (ROOT / "source/FishingAutomation/MainForm.Crafting.cs").read_text(encoding="utf-8")
        self.assertIn("MessageBoxDefaultButton.Button2", form)
        scheduler = (ROOT / "source/FishingAutomation/SchedulerRunMonitor.cs").read_text(encoding="utf-8")
        self.assertIn("progresslessDiagnostic", scheduler)
        retry = (ROOT / "source/FishingAutomation/dungeon/ScenarioEngine.AbyssRetry.cs").read_text(encoding="utf-8")
        self.assertIn("AbyssPassiveWaitAdvisory", retry)
        self.assertIn("추가 클릭/재입장/입력권 이전 없음", retry)
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("test_v3295_auto_update_policy.py", ci)

    def test_incident_stop_handoff_fails_closed(self):
        policy = (ROOT / "source/FishingAutomation/LifeSkillStopPolicy.cs").read_text(encoding="utf-8")
        bulk = (ROOT / "source/FishingAutomation/dungeon/InventoryBulkGatheringScreen.cs").read_text(encoding="utf-8")
        gather_test = (ROOT / "tests/gathering/Program.cs").read_text(encoding="utf-8")
        stability = (ROOT / "tests/stability/ProductionBehaviorBaseline.ps1").read_text(encoding="utf-8")
        self.assertIn("LifeSkillStopInputDecision", policy)
        self.assertIn("DecideStopInput", policy)
        self.assertIn("!isGathering && !isAutoTraveling && !isFishing && !stopButtonVisible", policy)
        self.assertIn("TryStopWithVerifiedEvidenceAsync", bulk)
        self.assertIn("if (await TryStopWithVerifiedEvidenceAsync(ct))", bulk)
        self.assertIn("await ConfirmLifeSkillStoppedAsync(displayName, ct)", bulk)
        self.assertIn("2026-11-05 576/576", gather_test)
        self.assertIn("post-Space policy does not trust stale MainButtonState", stability)
        publisher = PUBLISHER.read_text(encoding="utf-8")
        self.assertIn("Stop/UI CLI safe handoff and regression evidence missing", publisher)


    def test_post_update_schema_diagnostic_fails_closed(self):
        parser = (ROOT / "source/FishingAutomation/GatheringQueries.cs").read_text(encoding="utf-8")
        multi = (ROOT / "source/FishingAutomation/MainForm.MultiAltering.cs").read_text(encoding="utf-8")
        gathering = (ROOT / "tests/gathering/Program.cs").read_text(encoding="utf-8")
        publisher = PUBLISHER.read_text(encoding="utf-8")
        for term in ("private static JsonElement ActivityRequired",
                     "private static bool ActivityBool",
                     "private static string ActivityText",
                     "get_activity 필수 필드 누락:",
                     "get_activity 필드 유형 불일치:",
                     "게임 입력 차단"):
            self.assertIn(term, parser)
        self.assertIn("TryGetProperty(key, out var value)", parser)
        self.assertIn("[다중가공][예외 진단]", multi)
        self.assertIn("StackTrace(ex, false)", multi)
        for term in ("post-update missing Mode cannot authorize managed K",
                     "post-update missing Interaction cannot be treated as idle",
                     "post-update missing nested main-button state is diagnosed",
                     "post-update missing nested dungeon state fails closed",
                     "post-update changed Boolean to text fails closed",
                     "missing running interaction cannot authorize action"):
            self.assertIn(term, gathering)
        self.assertIn("get_activity schema diagnostics or fail-closed regression missing", publisher)



    def test_only_reviving_optional_and_safety_fences(self):
        parser = (ROOT / "source/FishingAutomation/GatheringQueries.cs").read_text(encoding="utf-8")
        gathering = (ROOT / "tests/gathering/Program.cs").read_text(encoding="utf-8")
        stability = (ROOT / "tests/stability/ProductionBehaviorBaseline.ps1").read_text(encoding="utf-8")
        travel = (ROOT / "source/FishingAutomation/AlteringTravelActivityPolicy.cs").read_text(encoding="utf-8")
        safety = (ROOT / "source/FishingAutomation/GatheringSafetyPolicy.cs").read_text(encoding="utf-8")
        publisher = PUBLISHER.read_text(encoding="utf-8")
        for token in ('bool? IsReviving', 'ActivityOptionalBool(r, "IsReviving", "$")',
                      'ActivityBool(r, "IsDead", "$")',
                      'ActivityBool(r, "IsInCombat", "$")',
                      'IsReviving != true', 'DescribeActivitySchema'):
            self.assertIn(token, parser)
        self.assertNotIn('ActivityBool(r, "IsReviving", "$")', parser)
        for token in ("missing IsReviving remains null/unknown",
                      "post-update string-typed IsReviving also fails closed",
                      "combat still prevents F9 input", "death remains unsafe",
                      "auto travel still blocks F9 managed input",
                      "bounded schema diagnostic never exposes account keys or values"):
            self.assertIn(token, gathering)
        self.assertIn("ShouldWaitForCombat", safety)
        self.assertIn("IsManagedSafeIdle", travel)
        self.assertIn("post-update IsReviving alone is optional", stability)
        self.assertIn("optional IsReviving only or unchanged safety guard regression missing", publisher)



    def test_real_herb_bottom_row_ocr_and_click_proof(self):
        list_policy = (ROOT / "source/FishingAutomation/LifeSkillListLayout.cs").read_text(encoding="utf-8")
        bulk = (ROOT / "source/FishingAutomation/dungeon/InventoryBulkGatheringScreen.cs").read_text(encoding="utf-8")
        gathering = (ROOT / "tests/gathering/Program.cs").read_text(encoding="utf-8")
        stability = (ROOT / "tests/stability/ProductionBehaviorBaseline.ps1").read_text(encoding="utf-8")
        publisher = PUBLISHER.read_text(encoding="utf-8")
        self.assertIn("VerifiedListSearchRoi", list_policy)
        self.assertIn("new(150, 145, 630, 835)", list_policy)
        self.assertIn("var listRoi = LifeSkillListLayout.VerifiedListSearchRoi;", bulk)
        self.assertNotIn("var listRoi = new Rectangle(150, 145, 630, 700)", bulk)
        for text in ("FindLifeSkillLabelAsync(", "HasRowIconVisual(frame, first.Value.Bounds)",
                     "HasRowIconVisual(fresh, second.Value.Bounds)",
                     "GatheringNavigationPolicy.IsStableFirstRow(first.Value.Bounds, second.Value.Bounds)",
                     "int maxPages = LifeSkillListLayout.NeverScroll",
                     "if (row is null)", "row.Value.Center.Y"):
            self.assertIn(text, bulk)
        for text in ("산뜻 버섯 bottom-row OCR included",
                     "herb bottom-row still uses bounded scroll",
                     "bottom-row requires two stable independent text boxes"):
            self.assertIn(text, gathering)
        self.assertIn("search extends below y930 but ends at y980", stability)
        self.assertIn("verified herb bottom-row ROI or two-frame OCR+icon input gate missing", publisher)
        self.assertIn("native-color herb row verification or safe row rejection regression missing", publisher)
        self.assertIn("tests/herb-row/Regression.csproj", (ROOT / '.github/workflows/ci.yml').read_text(encoding='utf-8'))




    def test_real_medicine_first_card_fix_scoped_and_safe(self):
        layout = (ROOT / "source/FishingAutomation/AlteringRecipeLayout.cs").read_text(encoding="utf-8")
        crafting = (ROOT / "source/FishingAutomation/CraftingHubLayout.cs").read_text(encoding="utf-8")
        screen = (ROOT / "source/FishingAutomation/dungeon/AlteringScreen.cs").read_text(encoding="utf-8")
        altering = (ROOT / "tests/altering/Program.cs").read_text(encoding="utf-8")
        integration = (ROOT / "tests/altering-screen-integration/Program.cs").read_text(encoding="utf-8")
        publisher = PUBLISHER.read_text(encoding="utf-8")
        self.assertIn("ProcessingSearchFirstResultPoint = new(218, 412)", layout)
        self.assertIn("ProductFirstResultPoint", crafting)
        self.assertIn("=> new(218, 613)", crafting)
        self.assertIn("AlteringRecipeLayout.ProcessingSearchFirstResultPoint", screen)
        self.assertIn("medicine search shares food dialog geometry but uses its observed first-card location", altering)
        self.assertIn("medicine first search result uses observed card center", integration)
        self.assertIn("actual medicine first card center or unchanged crafting coordinate regression missing", publisher)
        self.assertIn("No successful production CI for current main SHA", publisher)
        self.assertIn("main changed since CI; abort release", publisher)
        self.assertIn("ROLLBACK_PREVIOUS.cmd", publisher)


if __name__ == "__main__":
    unittest.main()
