"""V3.2.97 managed receipt onsite preflight / fail-closed release guard."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PUBLISHER = ROOT / ".github/workflows/release-v3297-auto-update.yml"


class V3297ManagedReceiptRelease(unittest.TestCase):
    def test_authorization_is_not_real_game_evidence(self):
        auth = json.loads((ROOT / "source/V3.2.97_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(auth["schema"], 1)
        self.assertEqual(auth["version"], "V3.2.97")
        self.assertEqual(auth["purpose"], "managed_receipt_current_onsite_preflight_release")
        self.assertEqual(auth["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(auth["request"], "수정하고 배포해")
        self.assertEqual(auth["sourcePullRequest"], 393)
        self.assertEqual(auth["sourcePullRequests"], [393])
        self.assertIs(auth["realGameTested"], False)
        for key in ("postUpdateGameTestingPending", "offlineRegressionRequired",
                    "mainCiRequired", "rollbackProvided"):
            self.assertIs(auth[key], True)
        updater = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.97"', updater)
        csproj = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for tag, value in (("Version", "3.2.97"), ("AssemblyVersion", "3.2.97.0"),
                           ("FileVersion", "3.2.97.0")):
            self.assertIn(f"<{tag}>{value}</{tag}>", csproj)

    def test_receipt_reuse_scene_preflight_precedes_generic_k_entry(self):
        screen = (ROOT / "source/FishingAutomation/dungeon/AlteringScreen.cs").read_text(encoding="utf-8")
        entry = screen.split("public async Task<bool> CollectAsyncAtBoundary(", 1)[1].split(
            "int? receiptWorkCountBefore;", 1)[0]
        self.assertLess(entry.index("await VerifyManagedReceiptReuseOnsiteAsync(plan, ct);"),
                        entry.index("await EnterFacilityAsync(plan, ct, directive);"))
        self.assertIn("if (directive == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite)", entry)
        preflight = screen.split("private async Task VerifyManagedReceiptReuseOnsiteAsync(", 1)[1].split(
            "public async Task<bool> CollectAsyncAtBoundary(", 1)[0]
        for token in ("const int maxObservations = 12", "FindFacilityHeaderAsync(",
                      "HasFacilityPanelStructureVisual(", "stable >= 2",
                      "TryAutoTravelingAsync(ct)", "autoTravel == false",
                      "AlteringCoordinatorFacilityMismatchException"):
            self.assertIn(token, preflight)
        for forbidden in ("TapFresh(", "ClickFresh(", "TapVerifiedFreshAsync(",
                          "ClickVerifiedFreshAsync(", "TravelToFacilityAsync(", "EnterFacilityAsync("):
            self.assertNotIn(forbidden, preflight)
        self.assertIn("[자동 가공][K 화면 인증 실패 진단]", screen)
        self.assertIn("K={kGlyph}", screen)
        self.assertIn("I={iGlyph}", screen)
        self.assertIn("Space={spaceGlyph}", screen)
        policy = (ROOT / "source/FishingAutomation/AlteringFacilityTravelConfirmPolicy.cs").read_text(encoding="utf-8")
        self.assertIn("IsManagedReceiptOnsiteFrame", policy)
        altering_tests = (ROOT / "tests/altering/Program.cs").read_text(encoding="utf-8")
        self.assertIn("F9 receipt preflight rejects stale lease", altering_tests)

    def test_one_shot_ci_and_checksum_release(self):
        w = PUBLISHER.read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]\n    paths:", w)
        self.assertIn("- '.github/workflows/release-v3297-auto-update.yml'", w)
        self.assertIn("  workflow_dispatch:", w)
        self.assertNotIn("  workflow_run:", w)
        for token in ("No successful production CI for current main SHA",
                      "Main production CI did not pass; refuse release",
                      "main changed since CI; abort release",
                      "V3.2.97 tag already exists",
                      "Get-FileHash $zip -Algorithm SHA256",
                      "Smoke test executable", "BUILD_COMMIT.txt",
                      "ROLLBACK_PREVIOUS.cmd", "tools/RollbackPrevious.ps1",
                      "MabiAuto_V3.2.97_Windows_Lite.zip",
                      "MabiAuto_V3.2.97_Windows_Lite.zip.sha256",
                      "MabiAuto_V3.2.97_Source.zip",
                      "gh release create V3.2.97", "--draft --verify-tag",
                      "Published release missing asset",
                      "$a.sourcePullRequest -ne 393",
                      "test_v3297_auto_update_policy.py"):
            self.assertIn(token, w)
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.97.md").is_file())


if __name__ == "__main__":
    unittest.main()
