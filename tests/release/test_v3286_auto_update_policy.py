"""Exact V3.2.86 release authorization and one-shot publisher checks.

These tests inspect source only; never launch games, create tags or publish.
"""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


class Phase1StabilityReleasePolicy(unittest.TestCase):
    def test_version_and_explicit_user_authorization(self):
        authorization = json.loads(
            (ROOT / "source/V3.2.86_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(authorization["schema"], 1)
        self.assertEqual(authorization["version"], "V3.2.86")
        self.assertEqual(authorization["purpose"], "phase1_stability_wait_advisories")
        self.assertEqual(authorization["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(authorization["request"], "이번꺼 까지 하고 정식배포해")
        self.assertEqual(authorization["sourcePullRequest"], 370)
        self.assertEqual(authorization["sourcePullRequests"], [367, 369, 370])
        for field in ("mainCiRequired", "offlineRegressionPassed", "rollbackProvided",
                      "postUpdateGameTestingPending"):
            self.assertIs(authorization[field], True)
        self.assertIs(authorization["realGameTested"], False)
        version = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.86"', version)
        project = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for field, value in (("Version","3.2.86"),("AssemblyVersion","3.2.86.0"),
                             ("FileVersion","3.2.86.0")):
            self.assertIn(f"<{field}>{value}</{field}>",project)

    def test_only_selected_main_push_triggers_new_publisher(self):
        workflow = (ROOT / ".github/workflows/release-v3286-auto-update.yml").read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]\n    paths:",workflow)
        self.assertIn("- '.github/workflows/release-v3286-auto-update.yml'",workflow)
        self.assertIn("  workflow_dispatch:",workflow)
        self.assertNotIn("  workflow_run:",workflow)
        self.assertIn("github.ref == 'refs/heads/main'",workflow)
        self.assertIn("No successful production CI for current main SHA",workflow)
        self.assertIn("Main production CI did not pass; refuse release",workflow)
        self.assertIn("main changed since CI; abort release",workflow)
        self.assertIn("V3.2.86 tag already exists",workflow)
        self.assertIn("Tag exists without release; manual recovery required",workflow)

    def test_binary_checksum_source_and_rollback_gates(self):
        workflow = (ROOT / ".github/workflows/release-v3286-auto-update.yml").read_text(encoding="utf-8")
        for token in (
                "Smoke test executable", "3.2.86.0",
                "MabiAuto_V3.2.86_Windows_Lite.zip",
                "MabiAuto_V3.2.86_Windows_Lite.zip.sha256",
                "MabiAuto_V3.2.86_Source.zip",
                "BUILD_COMMIT.txt", "Get-FileHash $zip -Algorithm SHA256",
                "ROLLBACK_PREVIOUS.cmd", "tools/RollbackPrevious.ps1",
                "gh release create V3.2.86", "--draft --verify-tag",
                "Published release missing asset",
                "Require V3.2.86 phase1 wait-advisory source and regressions",
                "tests/altering/Regression.csproj",
                "tests/crafting/Regression.csproj",
                "test_v3286_auto_update_policy.py",
        ):
            self.assertIn(token, workflow)
        self.assertIn("test_v3286_auto_update_policy.py",
                      (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8"))
        self.assertTrue((ROOT / "docs/PRODUCTION_WAIT_ADVISORIES_PHASE1.md").exists())
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.86.md").exists())


if __name__ == "__main__":
    unittest.main()
