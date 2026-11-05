"""V3.2.90 single-bundle release gates. Source-only; never starts a game or publishes."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PUBLISHER = ROOT / ".github/workflows/release-v3290-auto-update.yml"


class V3290SingleReleaseSafety(unittest.TestCase):
    def test_exact_user_authorization_and_versions(self):
        auth = json.loads((ROOT / "source/V3.2.90_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(auth["schema"], 1)
        self.assertEqual(auth["version"], "V3.2.90")
        self.assertEqual(auth["purpose"], "post_update_activity_schema_diagnostic_release")
        self.assertEqual(auth["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(auth["request"], "배포 까지 계속 진행해")
        self.assertEqual(auth["sourcePullRequest"], 379)
        self.assertEqual(auth["sourcePullRequests"], [379])
        for key in ("offlineRegressionRequired", "mainCiRequired", "rollbackProvided",
                    "postUpdateGameTestingPending"):
            self.assertIs(auth[key], True)
        self.assertIs(auth["realGameTested"], False)
        current = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        # Active publisher must have the exact updater binary version.
        self.assertIn('CurrentVersion = "V3.2.90"', current)
        csproj = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for field, value in (("Version", "3.2.90"), ("AssemblyVersion", "3.2.90.0"),
                             ("FileVersion", "3.2.90.0")):
            self.assertIn(f"<{field}>{value}</{field}>", csproj)

    def test_exact_main_ci_one_shot_and_rollbacks(self):
        w = PUBLISHER.read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]\n    paths:", w)
        self.assertIn("- '.github/workflows/release-v3290-auto-update.yml'", w)
        self.assertIn("  workflow_dispatch:", w)
        self.assertNotIn("  workflow_run:", w)
        for token in (
            "github.ref == 'refs/heads/main'",
            "No successful production CI for current main SHA",
            "Main production CI did not pass; refuse release",
            "main changed since CI; abort release",
            "V3.2.90 tag already exists",
            "Tag exists without release; manual recovery required",
            "Smoke test executable",
            "3.2.90.0",
            "MabiAuto_V3.2.90_Windows_Lite.zip",
            "MabiAuto_V3.2.90_Windows_Lite.zip.sha256",
            "MabiAuto_V3.2.90_Source.zip",
            "BUILD_COMMIT.txt",
            "Get-FileHash $zip -Algorithm SHA256",
            "ROLLBACK_PREVIOUS.cmd",
            "tools/RollbackPrevious.ps1",
            "gh release create V3.2.90",
            "--draft --verify-tag",
            "Published release missing asset",
            "$a.sourcePullRequest -ne 379",
            "Require V3.2.90 integrated restart and passive-watch regressions",
        ):
            self.assertIn(token, w)
        self.assertIn("test_v3290_auto_update_policy.py", w)
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.90.md").exists())

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
        self.assertIn("test_v3290_auto_update_policy.py", ci)

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




if __name__ == "__main__":
    unittest.main()
