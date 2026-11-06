"""V3.2.97 managed F9 receipt onsite revalidation and single-SHA release policy."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-v3297-auto-update.yml"


class V3297ReceiptReuse(unittest.TestCase):
    def test_exact_authorization_version_and_rollback(self):
        auth = json.loads((ROOT / "source/V3.2.97_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(auth["schema"], 1)
        self.assertEqual(auth["version"], "V3.2.97")
        self.assertEqual(auth["purpose"], "f9_verified_managed_receipt_reuse_navigation_fix")
        self.assertEqual(auth["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(auth["request"], "수정하고 배포해")
        self.assertEqual(auth["continuation"], "계속 진행해")
        self.assertEqual(auth["sourcePullRequest"], 394)
        self.assertEqual(auth["sourcePullRequests"], [394])
        self.assertIs(auth["realGameTested"], False)
        for field in ("postUpdateGameTestingPending", "offlineRegressionRequired",
                      "rollbackProvided", "mainCiRequired"):
            self.assertIs(auth[field], True)
        updater = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.97"', updater)
        project = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for tag, version in (("Version", "3.2.97"), ("AssemblyVersion", "3.2.97.0"),
                             ("FileVersion", "3.2.97.0")):
            self.assertIn(f"<{tag}>{version}</{tag}>", project)

    def test_reuse_preflight_before_generic_navigation_and_no_guessing(self):
        screen = (ROOT / "source/FishingAutomation/dungeon/AlteringScreen.cs").read_text(encoding="utf-8")
        policy = (ROOT / "source/FishingAutomation/AlteringReceiptPolicy.cs").read_text(encoding="utf-8")
        altering = (ROOT / "tests/altering/Program.cs").read_text(encoding="utf-8")
        start = screen.index("public async Task<bool> CollectAsyncAtBoundary(")
        collect = screen[start:screen.index("int? receiptWorkCountBefore;", start)]
        self.assertIn("if (directive == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite)", collect)
        first = collect.index("await RequireManagedReceiptReuseOnsiteAsync(plan, ct);")
        other = collect.index("await EnterFacilityAsync(plan, ct, directive);")
        self.assertLess(first, other)
        self.assertIn("else\\n            await EnterFacilityAsync", collect)

        start = screen.index("private async Task RequireManagedReceiptReuseOnsiteAsync(")
        end = screen.index("public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)", start)
        preflight = screen[start:end]
        for token in ("HasVerifiedFacilityEntryScreen(frame)",
                      "FindFacilityHeaderAsync(frame, plan.ScreenTitle, ct)",
                      "stableFrames >= 2", "RequireManagedIdleAsync",
                      "HasVerifiedFacilityEntryScreen(latest)",
                      "TryAutoTravelingAsync(ct)",
                      "CanReuseManagedReceiptOnsite",
                      "K/Esc/이동/수령 Space 입력 없이 정지",
                      "[자동 가공][수령 재사용 진단]"):
            self.assertIn(token, preflight)
        for forbidden in ("TapFresh(", "TapVerifiedFreshAsync(", "ClickFresh(",
                          "TravelToFacilityAsync(", "EnterFacilityAsync(", "beforeReceiveInput("):
            self.assertNotIn(forbidden, preflight)
        self.assertIn("consecutiveFacilityFrames >= 2", policy)
        self.assertIn("finalFacilityVerified", policy)
        self.assertIn("autoTraveling == false", policy)
        for token in ("CanReuseManagedReceiptOnsite(2, true, false)",
                      "CanReuseManagedReceiptOnsite(1, true, false)",
                      "CanReuseManagedReceiptOnsite(2, false, false)",
                      "CanReuseManagedReceiptOnsite(2, true, true)",
                      "CanReuseManagedReceiptOnsite(2, true, null)"):
            self.assertIn(token, altering)
        self.assertIn("HasCollectPromptAsync(finalFrame", screen)
        self.assertIn("await beforeReceiveInput(ct);", screen)

    def test_ci_and_release_never_publish_without_exact_sha(self):
        self.assertTrue(WORKFLOW.is_file())
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("Require V3.2.97 managed receipt on-site proof", workflow)
        self.assertIn("- '.github/workflows/release-v3297-auto-update.yml'", workflow)
        self.assertIn("  workflow_dispatch:", workflow)
        self.assertNotIn("  workflow_run:", workflow)
        for token in ("No successful production CI for current main SHA",
                      "Main production CI did not pass; refuse release",
                      "main changed since CI; abort release",
                      "V3.2.97 tag already exists",
                      "Get-FileHash $zip -Algorithm SHA256",
                      "Smoke test executable",
                      "ROLLBACK_PREVIOUS.cmd",
                      "MabiAuto_V3.2.97_Windows_Lite.zip",
                      "MabiAuto_V3.2.97_Windows_Lite.zip.sha256",
                      "MabiAuto_V3.2.97_Source.zip",
                      "gh release create V3.2.97",
                      "gh release edit V3.2.97 --draft=false",
                      "Published release missing asset",
                      "$a.sourcePullRequest -ne 394",
                      "test_v3297_auto_update_policy.py"):
            self.assertIn(token, workflow)
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("test_v3297_auto_update_policy.py", ci)
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.97.md").is_file())


if __name__ == "__main__":
    unittest.main()
