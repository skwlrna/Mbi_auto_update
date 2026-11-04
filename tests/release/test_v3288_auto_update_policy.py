"""V3.2.88 single-bundle release gates. Source-only; never starts a game or publishes."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PUBLISHER = ROOT / ".github/workflows/release-v3288-auto-update.yml"


class V3288SingleReleaseSafety(unittest.TestCase):
    def test_exact_user_authorization_and_versions(self):
        auth = json.loads((ROOT / "source/V3.2.88_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(auth["schema"], 1)
        self.assertEqual(auth["version"], "V3.2.88")
        self.assertEqual(auth["purpose"], "single_bundled_stability_release")
        self.assertEqual(auth["approvedBy"], "user_direct_request_in_chat")
        self.assertEqual(auth["request"], "한번에 업데이트 하지")
        self.assertEqual(auth["sourcePullRequest"], 376)
        self.assertEqual(auth["sourcePullRequests"], [376])
        for key in ("offlineRegressionRequired", "mainCiRequired", "rollbackProvided",
                    "postUpdateGameTestingPending"):
            self.assertIs(auth[key], True)
        self.assertIs(auth["realGameTested"], False)
        current = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        # Historical one-shot release stays immutable; only the active release
        # policy checks the executable's current version.
        if 'CurrentVersion = "V3.2.88"' not in current:
            return
        self.assertIn('CurrentVersion = "V3.2.88"', current)
        csproj = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        for field, value in (("Version", "3.2.88"), ("AssemblyVersion", "3.2.88.0"),
                             ("FileVersion", "3.2.88.0")):
            self.assertIn(f"<{field}>{value}</{field}>", csproj)

    def test_exact_main_ci_one_shot_and_rollbacks(self):
        w = PUBLISHER.read_text(encoding="utf-8")
        self.assertIn("  push:\n    branches: [main]\n    paths:", w)
        self.assertIn("- '.github/workflows/release-v3288-auto-update.yml'", w)
        self.assertIn("  workflow_dispatch:", w)
        self.assertNotIn("  workflow_run:", w)
        for token in (
            "github.ref == 'refs/heads/main'",
            "No successful production CI for current main SHA",
            "Main production CI did not pass; refuse release",
            "main changed since CI; abort release",
            "V3.2.88 tag already exists",
            "Tag exists without release; manual recovery required",
            "Smoke test executable",
            "3.2.88.0",
            "MabiAuto_V3.2.88_Windows_Lite.zip",
            "MabiAuto_V3.2.88_Windows_Lite.zip.sha256",
            "MabiAuto_V3.2.88_Source.zip",
            "BUILD_COMMIT.txt",
            "Get-FileHash $zip -Algorithm SHA256",
            "ROLLBACK_PREVIOUS.cmd",
            "tools/RollbackPrevious.ps1",
            "gh release create V3.2.88",
            "--draft --verify-tag",
            "Published release missing asset",
            "$a.sourcePullRequest -ne 376",
            "Require V3.2.88 integrated restart and passive-watch regressions",
        ):
            self.assertIn(token, w)
        self.assertIn("test_v3288_auto_update_policy.py", w)
        self.assertTrue((ROOT / "source/RELEASE_NOTES_V3.2.88.md").exists())

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
        self.assertIn("test_v3288_auto_update_policy.py", ci)


if __name__ == "__main__":
    unittest.main()
