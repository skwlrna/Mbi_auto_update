"""V3.3.33: a deferred Stop probe must not consume a Space transmission."""
from pathlib import Path
import json
import unittest

ROOT = Path(__file__).resolve().parents[2]
def read(path): return (ROOT / path).read_text(encoding="utf-8")

class GuardedGatherStopV3333(unittest.TestCase):
    def test_actual_input_count_and_fail_closed(self):
        screen = read("source/FishingAutomation/dungeon/InventoryBulkGatheringScreen.cs")
        policy = read("source/FishingAutomation/LifeSkillStopPolicy.cs")
        for part in ("bool initialStopSent = await TryStopWithVerifiedEvidenceAsync(ct)",
                     "initialStopSent ? 1 : 0", "sentStopInputs++",
                     "CanAttemptBoundedStop(", "TimeSpan.FromSeconds(3)",
                     "stableVisibleStop = stopVisible && activity.MainButtonState == \"Stop\"",
                     "IsStoppedAfterSpace(activity, stopVisible)",
                     "실제 Space 전송={sentStopInputs}/2"):
            self.assertIn(part, screen)
        self.assertIn("sentStopInputs is >= 0 and < 2", policy)
        self.assertIn("stableVerifiedStopFrames >= 2 && cooldownElapsed", policy)
        self.assertIn("return false;", screen)  # unverified input remains prohibited

    def test_authorized_gated_release(self):
        updater = read("source/FishingAutomation/UpdateManager.cs")
        project = read("source/FishingAutomation/FishingAutomation.csproj")
        self.assertTrue(any(f'CurrentVersion = "V{v}"' in updater
                            for v in ("3.3.33", "3.3.34", "3.3.35", "3.3.36", "3.3.37", "3.3.38", "3.3.39", "3.3.40", "3.3.41", "3.3.42", "3.3.43", "3.3.44", "3.3.45", "3.3.46")))
        self.assertTrue(any(f"<Version>{v}</Version>" in project
                            for v in ("3.3.33", "3.3.34", "3.3.35", "3.3.36", "3.3.37", "3.3.38", "3.3.39", "3.3.40", "3.3.41", "3.3.42", "3.3.43", "3.3.44", "3.3.45", "3.3.46")))
        a = json.loads(read("source/V3.3.33_RELEASE_AUTHORIZATION.json"))
        self.assertFalse(a["realGameTested"])
        self.assertEqual(a["purpose"], "verified_gathering_stop_actual_space_budget")
        release = read(".github/workflows/release-v3333-auto-update.yml")
        for part in ("No successful production CI for current main SHA",
                     "gh release create V3.3.33",
                     "MabiAuto_V3.3.33_Windows_Lite.zip.sha256",
                     "test_v3333_guarded_gather_stop.py"):
            self.assertIn(part, release)

if __name__ == "__main__":
    unittest.main()
