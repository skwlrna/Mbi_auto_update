"""V3.2.60 CI-gated Windows release: explicit user-approved unverified rollout.

Static safety checks. No claim of real Codex, game, Windows updater or rollback testing.
"""
from pathlib import Path
import json
import unittest

ROOT = Path(__file__).resolve().parents[2]
RELEASE = ROOT / ".github/workflows/release-v3260-auto-update.yml"
ATTESTATION = ROOT / "source/V3.2.60_GAME_TEST_ATTESTATION.json"
ACCEPTANCE = ROOT / "source/V3.2.60_SOLO_OWNER_RELEASE_ACCEPTANCE.json"


class V3260ReleasePolicy(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.workflow = RELEASE.read_text(encoding="utf-8")
        cls.att = json.loads(ATTESTATION.read_text(encoding="utf-8"))
        cls.accepted = json.loads(ACCEPTANCE.read_text(encoding="utf-8"))

    def test_exact_main_ci_gate_and_version(self):
        s = self.workflow
        for token in (
            'workflows: ["Check V3 Production Engine"]', 'types: [completed]',
            'branches: [main]', 'workflow_dispatch:',
            "workflow_run.conclusion == 'success'",
            "workflow_run.event == 'push'",
            "workflow_run.head_branch == 'main'",
            "workflow_run.repository.full_name == github.repository",
            "No successful production CI for current main SHA",
            "main changed since CI; abort release",
            'ref: ${{ github.event.workflow_run.head_sha || github.sha }}',
            'CurrentVersion = "V3\\.2\\.60"',
            "V3.2.60 tag already exists",
        ):
            self.assertIn(token, s)
        self.assertNotIn("  push:\n", s)
        self.assertIn('CurrentVersion = "V3.2.60"',
            (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8"))
        project = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for version in ("<Version>3.2.60</Version>",
                        "<AssemblyVersion>3.2.60.0</AssemblyVersion>",
                        "<FileVersion>3.2.60.0</FileVersion>"):
            self.assertIn(version, project)

    def test_unverified_user_request_not_misrepresented_as_game_success(self):
        self.assertEqual(self.att["version"], "V3.2.60")
        for key in ("status", "aiWatcherRealRepair", "aiWorkspaceRecovery",
                    "oneClickOfflineSmoke", "rollbackVerified",
                    "imageTransmissionOnPc", "reviewReasonDisplayOnPc"):
            self.assertEqual(self.att[key], "pending")
        self.assertEqual(self.accepted["rolloutMode"], "user_requested_unverified_release")
        self.assertEqual(self.accepted["releasePurpose"],
                         "ai_autofix_photo_evidence_review_reasons_pr294")
        self.assertEqual(self.accepted["acceptanceSource"], "user_direct_request_in_chat")
        self.assertEqual(self.accepted["scope"], "personal_use_only")
        self.assertIs(self.accepted["publicUpdaterReleaseApproved"], True)
        self.assertIs(self.accepted["allowFabricatedGameTestAttestation"], False)
        for key in ("aiWatcherRealRepair", "aiWorkspaceRecovery", "aiGameBehavior",
                    "rollbackExecution", "screenshotToCodexOnPc", "reviewReasonDisplayOnPc",
                    "retryNoPatchOnPc"):
            self.assertEqual(self.accepted[key], "not_tested")
        for marker in (
            "source/V3.2.60_GAME_TEST_ATTESTATION.json",
            "source/V3.2.60_SOLO_OWNER_RELEASE_ACCEPTANCE.json",
            "Real-game testing or rollback not verified; auto-update blocked",
            "Production source changed after tested candidate",
            "Required update/rollback asset missing",
        ):
            self.assertIn(marker, self.workflow)

    def test_binary_integrity_rollback_source_and_draft_promotion(self):
        s = self.workflow
        self.assertEqual(s.count("gh release create V3.2.60 "), 1)
        self.assertEqual(s.count("gh release edit V3.2.60 --draft=false"), 1)
        for part in ("MabiAuto_V3.2.60_Windows_Lite.zip",
                     "MabiAuto_V3.2.60_Windows_Lite.zip.sha256",
                     "MabiAuto_V3.2.60_Source.zip",
                     "BUILD_COMMIT.txt", "ROLLBACK_PREVIOUS.cmd",
                     "RollbackPrevious.ps1", "release/ai-autopilot/autopilot.py",
                     "release/ai-autopilot/supervised_codex.py",
                     "release/ai-autopilot/worktree_recovery.py"):
            self.assertIn(part, s)
        self.assertLess(s.index("Require game signoff OR direct-user-requested"),
                        s.index("Publish verified Windows binaries"))
        self.assertLess(s.index("Smoke test executable"),
                        s.index("Publish public auto-update Release"))
        self.assertIn("main changed since CI; abort release", s)
        self.assertIn("Get-FileHash $zip -Algorithm SHA256", s)

    def test_screenshots_require_opt_in_and_are_not_merged_or_auto_deployed(self):
        watcher = (ROOT / "source/FishingAutomation/ai-autopilot/autopilot.py").read_text(encoding="utf-8")
        ui = (ROOT / "source/FishingAutomation/AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        for marker in ("consented_error_image", "local_runtime_screenshot_to_codex",
                       "runtime_reports_dir", 'args += ["--image", str(image)]'):
            self.assertIn(marker, watcher)
        self.assertIn("오류 사진을 Codex에 전달 (동의 필요)", ui)
        self.assertIn("BuildReviewCauseSummary(analyses)", ui)
        self.assertIn("기존 오류 사진을 자동으로 가져오지 않으며", ui)


if __name__ == "__main__":
    unittest.main()
