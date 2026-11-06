"""V3.2.96 independent-lane wait and fail-closed release policy."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-v3296-auto-update.yml"


class V3296LaneYieldRelease(unittest.TestCase):
    def test_user_authorization_and_version(self):
        auth = json.loads((ROOT / "source/V3.2.96_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(auth["schema"], 1)
        self.assertEqual(auth["version"], "V3.2.96")
        self.assertEqual(auth["purpose"], "f9_cooperative_independent_facility_wait_release")
        self.assertEqual(auth["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(auth["request"], "수정하고 배포해")
        self.assertEqual(auth["sourcePullRequest"], 392)
        self.assertEqual(auth["sourcePullRequests"], [392])
        self.assertIs(auth["realGameTested"], False)
        for key in ("postUpdateGameTestingPending", "offlineRegressionRequired",
                    "mainCiRequired", "rollbackProvided"):
            self.assertIs(auth[key], True)
        updater = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.96"', updater)
        project = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for field, value in (("Version", "3.2.96"), ("AssemblyVersion", "3.2.96.0"),
                             ("FileVersion", "3.2.96.0")):
            self.assertIn(f"<{field}>{value}</{field}>", project)

    def test_compatibility_and_safe_deferred_dependency(self):
        dependency = (ROOT / "source/FishingAutomation/MultiAlteringDependencyScheduler.cs").read_text(encoding="utf-8")
        coordinator = (ROOT / "source/FishingAutomation/MultiAlteringCoordinator.cs").read_text(encoding="utf-8")
        parent = (ROOT / "source/FishingAutomation/MainForm.MultiAltering.cs").read_text(encoding="utf-8")
        altering = (ROOT / "tests/altering/Program.cs").read_text(encoding="utf-8")
        for key in ("IntermediateFacilityBusyException", "yieldBusyFacilityToParent",
                    "allowCooperativeYield: true", "throw new IntermediateFacilityBusyException"):
            self.assertIn(key, dependency)
        self.assertIn("waitingForDependency", coordinator)
        self.assertIn("catch (IntermediateFacilityBusyException dependencyBusy)", coordinator)
        self.assertIn("AssertAccess(facility, _laneOwner)", coordinator)
        self.assertIn("allowShrink: false", coordinator)
        self.assertIn("if (!currentRecipe.Alterable &&", coordinator)
        self.assertIn("globalQueueBlocked", coordinator)
        self.assertIn("catch (IntermediateFacilityBusyException)", parent)
        self.assertIn("ConfirmBatchReturn(workTicket, 0, false)", parent)
        self.assertIn("F9 intermediate batch wait yields before inputs", altering)
        self.assertIn("F9 yielding dependency boundary never registers, receives, or leases", altering)
        telegram = (ROOT / "source/FishingAutomation/MultiAlteringTelegramStatus.cs").read_text(encoding="utf-8")
        status = (ROOT / "source/FishingAutomation/MainForm.AlteringStatus.cs").read_text(encoding="utf-8")
        self.assertIn("제법 CLI(조회 시점)", telegram)
        self.assertIn("GetAlterableItemsAsync", status)
        self.assertIn("ParseRecipes(response)", status)

    def test_single_sha_ci_gated_release(self):
        w = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]\n    paths:", w)
        self.assertIn("- '.github/workflows/release-v3296-auto-update.yml'", w)
        self.assertIn("  workflow_dispatch:", w)
        self.assertNotIn("  workflow_run:", w)
        for term in ("No successful production CI for current main SHA",
                     "Main production CI did not pass; refuse release",
                     "main changed since CI; abort release",
                     "V3.2.96 tag already exists",
                     "Get-FileHash $zip -Algorithm SHA256",
                     "Smoke test executable", "BUILD_COMMIT.txt",
                     "ROLLBACK_PREVIOUS.cmd", "tools/RollbackPrevious.ps1",
                     "MabiAuto_V3.2.96_Windows_Lite.zip",
                     "MabiAuto_V3.2.96_Windows_Lite.zip.sha256",
                     "MabiAuto_V3.2.96_Source.zip",
                     "gh release create V3.2.96", "--draft --verify-tag",
                     "Published release missing asset",
                     "$a.sourcePullRequest -ne 392",
                     "test_v3296_auto_update_policy.py"):
            self.assertIn(term, w)
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.96.md").is_file())


if __name__ == "__main__":
    unittest.main()
