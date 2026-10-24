"""Static CI-only pipeline safety contract: no API, publishing or game actions."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]


class ApprovalReleaseChainTests(unittest.TestCase):
    def read(self, path):
        return (ROOT / path).read_text(encoding="utf-8-sig")

    def test_static_publisher_requires_exact_approved_patch(self):
        pub = self.read(".github/workflows/publish-reviewed-repair-auto-update.yml")
        required = (
            "workflow_run:",
            "workflow_dispatch') &&",
            "Require an exact, reviewed CI-only repair version",
            "if: steps.approval.outputs.reviewed == 'true'",
            "reviewed=true",
            "real_game_verified -ne $false",
            "operator_reviewed -ne $true",
            "pr.merge_commit_sha -ne $record.reviewed_merge_sha",
            "git diff --name-only $record.reviewed_merge_sha HEAD",
            "Not the immediately next semantic version",
            "No reviewed repair approval on this commit: release skipped",
            "No successful production CI for current main SHA",
            "workflow_dispatch') -and",
            "gh release create",
            "RollbackPrevious.ps1",
            "Get-FileHash $zip -Algorithm SHA256",
            "VersionInfo.FileVersion",
            "Smoke test executable",
            "Windows_Lite.zip.sha256",
        )
        for phrase in required:
            with self.subTest(phrase=phrase):
                self.assertIn(phrase, pub)
        self.assertNotIn("git tag V3.2.62", pub)
        # GitHub rejects the entire YAML before jobs can run if a step has
        # the same id key twice, even if the rest of the CI-only flow passes.
        ids = [line.split(":", 1)[1].strip() for line in pub.splitlines()
               if line.startswith("        id: ")]
        self.assertEqual(len(ids), len(set(ids)), "Duplicate workflow step IDs")
        self.assertEqual(ids, ["approval", "eligibility"])

    def test_release_prep_is_workflow_token_safe(self):
        wf = self.read(".github/workflows/prepare-reviewed-repair-release.yml")
        script = self.read("tools/mabi-auto-repair/prepare_release.py")
        for phrase in (
            "PYTHONDONTWRITEBYTECODE: '1'",
            "git status --porcelain).Count -ne 4",
            "git diff --cached --name-only).Count -ne 4",
            "gh workflow run ci.yml",
            "git push origin HEAD:refs/heads/main",
            "head.sha -ne $env:APPROVED_HEAD",
        ):
            self.assertIn(phrase, wf)
        self.assertIn('"release_workflow": "publish-reviewed-repair-auto-update.yml"', script)
        self.assertNotIn('".github/workflows/ci.yml": ci', script)
        self.assertNotIn('release_path: release', script)
        self.assertNotIn(".github/workflows/release-v3262-auto-update.yml", script)

    def test_ci_is_dynamic_across_successive_patch_versions(self):
        ci = self.read(".github/workflows/ci.yml")
        self.assertIn("workflow_dispatch:", ci)
        self.assertIn("publish-reviewed-repair-auto-update.yml", ci)
        self.assertNotIn("if ($p -notmatch '<Version>3\\.2\\.62</Version>')", ci)

    def test_auto_updater_keeps_active_game_jobs_and_recovers(self):
        main = self.read("source/FishingAutomation/MainForm.cs")
        updater = self.read("source/FishingAutomation/MainForm.Update.cs")
        apply = self.read("source/tools/ApplyUpdate.ps1")
        self.assertIn("CheckApprovedReleaseWhenIdle();", main)
        self.assertIn("_updateCheckRunning || AnyRunning", updater)
        self.assertIn("string zip = await UpdateManager.DownloadAsync(info, progress);", updater)
        self.assertIn("if (AnyRunning)", updater.split(
            "string zip = await UpdateManager.DownloadAsync(info, progress);", 1)[1])
        self.assertIn("File.Delete(zip)", updater)
        self.assertIn("Start-MabiAuto", apply)
        self.assertIn("New version startup health check FAILED. Rolling back.", apply)
        self.assertIn(".previous_version", apply)


if __name__ == "__main__":
    unittest.main()
