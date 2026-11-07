"""V3.2.98 F07 bounded two-frame read-only retry and exact-SHA public release gate."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/release-v3298-auto-update.yml"


class V3298F07ReleasePolicy(unittest.TestCase):
    def test_authorization_and_versions(self):
        auth = json.loads((ROOT / "source/V3.2.98_RELEASE_AUTHORIZATION.json").read_text(encoding="utf-8"))
        self.assertEqual(auth["version"], "V3.2.98")
        self.assertEqual(auth["purpose"], "f07_bounded_fresh_recipe_identity_retry")
        self.assertEqual(auth["request"], "수정하고 배포해")
        self.assertEqual(auth["sourcePullRequest"], 395)
        self.assertEqual(auth["sourcePullRequests"], [395])
        self.assertIs(auth["realGameTested"], False)
        for key in ("offlineRegressionRequired", "mainCiRequired", "rollbackProvided",
                    "postUpdateGameTestingPending"):
            self.assertIs(auth[key], True)
        updater = (ROOT / "source/FishingAutomation/UpdateManager.cs").read_text(encoding="utf-8")
        project = (ROOT / "source/FishingAutomation/FishingAutomation.csproj").read_text(encoding="utf-8")
        self.assertIn('CurrentVersion = "V3.2.98"', updater)
        for tag, version in (("Version", "3.2.98"), ("AssemblyVersion", "3.2.98.0"),
                             ("FileVersion", "3.2.98.0")):
            self.assertIn(f"<{tag}>{version}</{tag}>", project)

    def test_identity_and_safe_retry(self):
        screen = (ROOT / "source/FishingAutomation/dungeon/AlteringScreen.cs").read_text(encoding="utf-8")
        policy = (ROOT / "source/FishingAutomation/AlteringRecipeIdentityPolicy.cs").read_text(encoding="utf-8")
        tests = (ROOT / "tests/altering/Program.cs").read_text(encoding="utf-8")
        start = screen.index("// F07: each bounded attempt still requires TWO independent")
        end = screen.index("visualActionCenter = priorVerifiedCenter;", start)
        gate = screen[start:end]
        for marker in ("VerifyTwoFreshObservationsWithRetriesAsync",
                       "IsRecipeDetailStructureAsync(fresh",
                       "FindRecipeAsync(fresh, plan",
                       "DetectRemoteProcessStateAsync(fresh",
                       "TryFindFreeProcessButtonVisual(fresh",
                       "IsStableFreshFreeActionTarget(",
                       "priorVerifiedCenter = Point.Empty",
                       "bool valid = detail && identity && !remote && button && stable",
                       "if (!exactDetail)", "3,"):
            self.assertIn(marker, gate)
        self.assertNotIn("ClickFresh(", gate)
        self.assertNotIn("TapFresh(", gate)
        self.assertIn("maxAttempts > 3", policy)
        self.assertIn("VerifyTwoFreshObservationsAsync(", policy)
        self.assertIn("F07 alternating single-frame positives never authorize processing", tests)
        self.assertIn("F07 permanent mismatch stops after three bounded attempts", tests)
        self.assertIn("ClickVerifiedFreshAsync(liveCenter", screen)

    def test_exact_main_ci_and_release_safety(self):
        self.assertTrue(WORKFLOW.is_file())
        wf = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("- '.github/workflows/release-v3298-auto-update.yml'", wf)
        self.assertIn("  workflow_dispatch:", wf)
        self.assertNotIn("  workflow_run:", wf)
        for marker in ("Require V3.2.98 F07 fresh-pair identity",
                       "No successful production CI for current main SHA",
                       "Main production CI did not pass; refuse release",
                       "main changed since CI; abort release",
                       "V3.2.98 tag already exists",
                       "Smoke test executable", "Get-FileHash $zip -Algorithm SHA256",
                       "ROLLBACK_PREVIOUS.cmd", "tools/RollbackPrevious.ps1",
                       "MabiAuto_V3.2.98_Windows_Lite.zip.sha256",
                       "MabiAuto_V3.2.98_Source.zip",
                       "gh release create V3.2.98",
                       "gh release edit V3.2.98 --draft=false",
                       "Published release missing asset"):
            self.assertIn(marker, wf)
        self.assertLess(wf.index("Smoke test executable"), wf.index("Publish public auto-update Release"))
        ci = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("test_v3298_auto_update_policy.py", ci)
        self.assertIn("2026-11-07", (ROOT / "source/RELEASE_NOTES_V3.2.98.md").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
