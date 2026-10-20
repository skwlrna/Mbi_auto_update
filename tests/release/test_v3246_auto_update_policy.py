"""V3.2.46: the public GitHub updater can only run from a verified main CI.
This is a source-only contract test. It never logs in, creates releases, or
changes installed macro state.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-v3246-auto-update.yml"
ATTESTATION = ROOT / "source/V3.2.46_GAME_TEST_ATTESTATION.json"
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
        self.assertIn("V3.2.46 tag already exists", source)
        self.assertIn("CurrentVersion = \\"V3\\.2\\.46\\"", source)
        self.assertIn('public const string CurrentVersion = "V3.2.46"', self.updater)
        self.assertIn("<Version>3.2.46</Version>", self.project)

    def test_real_game_and_rollback_remain_non_bypassable(self):
        source = self.workflow
        for required in ("V3.2.46_GAME_TEST_ATTESTATION.json",
                         "$att.status -ne 'passed'",
                         "$att.f9FoodHoldResume -ne 'passed'",
                         "$att.f9ReceiptNoDuplicate -ne 'passed'",
                         "$att.oneClickOfflineSmoke -ne 'passed'",
                         "$att.rollbackVerified -ne 'passed'",
                         "Tested commit not ancestor of release",
                         "Production source changed after tested candidate"):
            self.assertIn(required, source)
        self.assertLess(source.index("Require verified real-game recovery"),
                        source.index("Publish verified Windows binaries"))
        self.assertLess(source.index("Require verified real-game recovery"),
                        source.index("Publish public auto-update Release"))

    def test_updater_compatible_release_and_backups(self):
        source = self.workflow
        self.assertEqual(source.count("gh release create V3.2.46 "), 1)
        self.assertEqual(source.count("gh release edit V3.2.46 --draft=false"), 1)
        self.assertIn("MabiAuto_V3.2.46_Windows_Lite.zip", source)
        self.assertIn("MabiAuto_V3.2.46_Windows_Lite.zip.sha256", source)
        self.assertIn("BUILD_COMMIT.txt", source)
        self.assertIn("ROLLBACK_PREVIOUS.cmd", source)
        self.assertIn("RollbackPrevious.ps1", source)
        self.assertIn("one_click_test.py", source)


if __name__ == "__main__":
    unittest.main()
