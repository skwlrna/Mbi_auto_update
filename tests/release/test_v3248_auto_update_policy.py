"""V3.2.48: public rollout requires exact main CI and explicit acceptance.
A sole-operator opt-in is not a simulated real-game attestation.
This is a source-only test: no login, releases or local macro updates.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-v3248-auto-update.yml"
ATTESTATION = ROOT / "source/V3.2.48_GAME_TEST_ATTESTATION.json"
UPDATER = ROOT / "source/FishingAutomation/UpdateManager.cs"
PROJECT = ROOT / "source/FishingAutomation/FishingAutomation.csproj"


class StableReleaseSafetyContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = WORKFLOW.read_text(encoding="utf-8")
        cls.updater = UPDATER.read_text(encoding="utf-8")
        cls.project = PROJECT.read_text(encoding="utf-8")

    def test_automatic_publish_requires_only_successful_main_push_ci(self):
        source = self.workflow
        self.assertIn('workflows: ["Check V3 Production Engine"]', source)
        self.assertIn("types: [completed]", source)
        self.assertIn("branches: [main]", source)
        self.assertIn("workflow_dispatch:", source)
        self.assertNotIn("  push:\n", source)
        guard = re.search(r"^    if: (.+)$", source, re.M)
        self.assertIsNotNone(guard)
        for required in ("workflow_run.conclusion == 'success'",
                         "workflow_run.event == 'push'",
                         "workflow_run.head_branch == 'main'",
                         "workflow_run.repository.full_name == github.repository"):
            self.assertIn(required, guard.group(1))

    def test_source_and_binaries_refer_to_exact_successful_ci_head(self):
        source = self.workflow
        self.assertIn("ref: ${{ github.event.workflow_run.head_sha || github.sha }}", source)
        self.assertEqual(source.count("CI_HEAD_SHA: ${{ github.event.workflow_run.head_sha || github.sha }}"), 2)
        self.assertIn("No successful production CI for current main SHA", source)
        self.assertIn("main changed since CI; abort release", source)
        self.assertIn("V3.2.48 tag already exists", source)
        self.assertIn(r'CurrentVersion = "V3\.2\.47"', source)
        self.assertIn('public const string CurrentVersion = "V3.2.48"', self.updater)
        self.assertIn("<Version>3.2.48</Version>", self.project)

    def test_unverified_solo_operator_release_has_explicit_acceptance_not_fake_signoff(self):
        import json
        source = self.workflow
        att = json.loads(ATTESTATION.read_text(encoding="utf-8"))
        self.assertEqual(att["status"], "pending")
        self.assertEqual(att["rollbackVerified"], "pending")
        solo = json.loads((ROOT / "source/V3.2.48_SOLO_OWNER_RELEASE_ACCEPTANCE.json")
                          .read_text(encoding="utf-8"))
        self.assertEqual(solo["rolloutMode"],
                         "user_requested_unverified_release")
        self.assertEqual(solo["gameF9FoodHoldResume"], "not_tested")
        self.assertEqual(solo["gameF9ReceiptNoDuplicate"], "not_tested")
        self.assertEqual(solo["rollbackExecution"], "not_tested")
        self.assertIs(solo["allowFabricatedGameTestAttestation"], False)
        self.assertEqual(solo["pcOfflineSmoke"], "not_tested_for_v3248")
        self.assertEqual(solo["aiWatcher"], "not_tested_for_v3248")
        self.assertEqual(solo["bagWeightLiveCli"], "not_tested")
        self.assertEqual(solo["bagWeightGatherStop"], "not_tested")
        self.assertEqual(solo["acceptanceSource"], "user_direct_request_in_chat")
        self.assertIs(solo["publicUpdaterReleaseApproved"], True)
        for required in ("V3.2.48_GAME_TEST_ATTESTATION.json",
                         "V3.2.48_SOLO_OWNER_RELEASE_ACCEPTANCE.json",
                         "$att.status -eq 'passed'",
                         "$att.f9FoodHoldResume -eq 'passed'",
                         "$att.f9ReceiptNoDuplicate -eq 'passed'",
                         "$att.rollbackVerified -eq 'passed'",
                         "Tested commit not ancestor of release",
                         "Production source changed after tested candidate",
                         "$att.status -ne 'pending'",
                         "$solo.allowFabricatedGameTestAttestation -ne $false",
                         "$solo.gameF9FoodHoldResume -ne 'not_tested'",
                         "$solo.gameF9ReceiptNoDuplicate -ne 'not_tested'",
                         "$solo.rollbackExecution -ne 'not_tested'",
                         "Real-game testing or rollback not verified; auto-update blocked",
                         "Required update/rollback asset missing"):
            self.assertIn(required, source)
        self.assertLess(source.index("Require game signoff OR direct-user-requested"),
                        source.index("Publish verified Windows binaries"))
        self.assertLess(source.index("Require game signoff OR direct-user-requested"),
                        source.index("Publish public auto-update Release"))

    def test_powershell_warning_quote_is_balanced(self):
        warnings = [line.split("Write-Warning ", 1)[1].strip()
                    for line in self.workflow.splitlines()
                    if "Write-Warning " in line]
        self.assertEqual(len(warnings), 1)
        warning = warnings[0]
        self.assertTrue(warning.startswith("'") and warning.endswith("'"))
        self.assertNotIn("'", warning[1:-1].replace("''", ""))

    def test_updater_compatible_release_and_backups(self):
        source = self.workflow
        self.assertEqual(source.count("gh release create V3.2.48 "), 1)
        self.assertEqual(source.count("gh release edit V3.2.48 --draft=false"), 1)
        self.assertIn("MabiAuto_V3.2.48_Windows_Lite.zip", source)
        self.assertIn("MabiAuto_V3.2.48_Windows_Lite.zip.sha256", source)
        self.assertIn("BUILD_COMMIT.txt", source)
        self.assertIn("ROLLBACK_PREVIOUS.cmd", source)
        self.assertIn("RollbackPrevious.ps1", source)
        self.assertIn("one_click_test.py", source)
        self.assertIn("CurrentInventoryWeightAsDecimal", (ROOT / "source/FishingAutomation/GatheringQueries.cs").read_text(encoding="utf-8"))
        self.assertIn("GatheringWeightPolicy.Read", (ROOT / "source/FishingAutomation/GatheringAutomation.cs").read_text(encoding="utf-8"))
        self.assertIn("GatheringWeightPolicy.cs", (ROOT / "tests/gathering/Regression.csproj").read_text(encoding="utf-8"))
        self.assertIn("CraftingQueueCoordinator.RunAsync", (ROOT / "source/FishingAutomation/MainForm.Crafting.cs").read_text(encoding="utf-8"))
        self.assertIn("CraftingGatherCompletionPolicy.HasVerifiedTarget", (ROOT / "source/FishingAutomation/dungeon/CraftingScreen.cs").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
