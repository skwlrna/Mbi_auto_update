"""V3.2.59: strict CI-gated public Windows release with explicit sole-operator opt-in.
Source-only checks. No false live-game attestation or offline-to-game equivalence.
"""
from pathlib import Path
import json
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
WF = ROOT / ".github/workflows/release-v3259-auto-update.yml"
AT = ROOT / "source/V3.2.59_GAME_TEST_ATTESTATION.json"
AC = ROOT / "source/V3.2.59_SOLO_OWNER_RELEASE_ACCEPTANCE.json"

class V3259ReleaseSafetyContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.wf = WF.read_text(encoding="utf-8")
        cls.att = json.loads(AT.read_text(encoding="utf-8"))
        cls.accept = json.loads(AC.read_text(encoding="utf-8"))

    def test_exact_main_ci_and_source(self):
        s = self.wf
        self.assertIn('workflows: ["Check V3 Production Engine"]', s)
        self.assertIn('types: [completed]', s)
        self.assertIn('branches: [main]', s)
        self.assertIn('workflow_dispatch:', s)
        self.assertNotIn('  push:\n', s)
        for key in ("workflow_run.conclusion == 'success'",
                    "workflow_run.event == 'push'",
                    "workflow_run.head_branch == 'main'",
                    "workflow_run.repository.full_name == github.repository",
                    "No successful production CI for current main SHA",
                    "main changed since CI; abort release",
                    "ref: ${{ github.event.workflow_run.head_sha || github.sha }}",
                    "V3.2.59 tag already exists",
                    'CurrentVersion = "V3\\.2\\.59"'):
            self.assertIn(key, s)
        self.assertEqual(s.count("CI_HEAD_SHA: ${{ github.event.workflow_run.head_sha || github.sha }}"),2)
        # This contract is about the historical V3.2.59 release. Once a new
        # version is published, the installed version can advance while the
        # immutable V3.2.59 workflow/attestation requirements remain intact.
        updater = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        project = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        match = re.search(r'CurrentVersion = "V(\\d+)\\.(\\d+)\\.(\\d+)"', updater)
        self.assertIsNotNone(match)
        parts = tuple(int(value) for value in match.groups())
        self.assertGreaterEqual(parts, (3, 2, 59))
        self.assertIn(f'<Version>{".".join(str(value) for value in parts)}</Version>', project)

    def test_real_game_pending_not_falsified(self):
        self.assertEqual(self.att["version"], "V3.2.59")
        self.assertEqual(self.att["status"], "pending")
        self.assertEqual(self.att["rollbackVerified"], "pending")
        self.assertEqual(self.accept["releasePurpose"],"facility_field_false_positive_move_click_guard_pr292")
        self.assertEqual(self.accept["acceptedOnLocalDate"],"2026-10-24")
        self.assertEqual(self.accept["rolloutMode"],"user_requested_unverified_release")
        self.assertEqual(self.accept["acceptanceSource"],"user_direct_request_in_chat")
        self.assertEqual(self.accept["scope"],"personal_use_only")
        self.assertIs(self.accept["publicUpdaterReleaseApproved"],True)
        self.assertIs(self.accept["allowFabricatedGameTestAttestation"],False)
        for k in ("aiWatcherRealRepair","aiGameBehavior","rollbackExecution",
                  "aiWorkspaceRecovery","facilityFieldVetoRealGame",
                  "facilityMovePreClickRealGame","managedReceiptRealGame"):
            self.assertEqual(self.accept[k],"not_tested")
        for key in ("V3.2.59_GAME_TEST_ATTESTATION.json",
                    "V3.2.59_SOLO_OWNER_RELEASE_ACCEPTANCE.json",
                    "$att.status -eq 'passed'","$att.status -ne 'pending'",
                    "$att.aiWatcherRealRepair -eq 'passed'",
                    "$att.rollbackVerified -eq 'passed'",
                    "Tested commit not ancestor of release",
                    "Production source changed after tested candidate",
                    "Real-game testing or rollback not verified; auto-update blocked",
                    "Required update/rollback asset missing"):
            self.assertIn(key, self.wf)

    def test_binary_integrity_rollback_and_draft_to_public(self):
        s = self.wf
        self.assertEqual(s.count("gh release create V3.2.59 "),1)
        self.assertEqual(s.count("gh release edit V3.2.59 --draft=false"),1)
        for f in ("MabiAuto_V3.2.59_Windows_Lite.zip",
                  "MabiAuto_V3.2.59_Windows_Lite.zip.sha256",
                  "MabiAuto_V3.2.59_Source.zip",
                  "BUILD_COMMIT.txt","ROLLBACK_PREVIOUS.cmd",
                  "RollbackPrevious.ps1","one_click_test.py",
                  "release/ai-autopilot/worktree_recovery.py",
                  "managed_home_key.png","managed_home_mail_key.png",
                  "managed_end_key.png","managed_escape_key.png"):
            self.assertIn(f,s)
        self.assertLess(s.index("Require game signoff OR direct-user-requested"),
                        s.index("Publish verified Windows binaries"))
        self.assertLess(s.index("Smoke test executable"),s.index("Publish public auto-update Release"))
        warnings=[line.split("Write-Warning ",1)[1].strip() for line in s.splitlines()
                  if "Write-Warning " in line]
        self.assertEqual(len(warnings),1)
        self.assertTrue(warnings[0].startswith("'") and warnings[0].endswith("'"))

    def test_original_pr292_ui_proof_retained(self):
        screen=(ROOT/"source/FishingAutomation/dungeon/AlteringScreen.cs").read_text(encoding="utf-8")
        tests=(ROOT/"tests/altering-screen-integration/Program.cs").read_text(encoding="utf-8")
        for token in ("HasWorldHudForFacilityVeto", "HasVerifiedFacilityMoveClickScreen",
                      "HasFacilityPanelStructureVisual","HasFacilityQueueSlotStructureVisual",
                      "ClickVerifiedFreshAsync"):
            self.assertIn(token,screen)
        for token in ("field-metal-20261024-033339.png",
                      "remote-wood-20261007-172137.png",
                      "HasVerifiedFacilityMoveClickScreen", "ClickVerifiedFreshAsync"):
            self.assertIn(token,tests)

if __name__ == "__main__":
    unittest.main()
