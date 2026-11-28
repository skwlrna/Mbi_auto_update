from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]


def read(name):
    return (ROOT / name).read_text(encoding="utf-8")


class GatheringTotalStockV3330(unittest.TestCase):
    def test_queue_ui_is_bounded_and_explicit(self):
        source = read("source/FishingAutomation/MainForm.ProductionPage.cs")
        for token in (
            "GatheringQueueChoice",
            "QueuedGatheringPlans",
            "AddCurrentGatheringQueue",
            "RemoveSelectedGatheringQueue",
            "ClearQueuedGatheringPlans",
            "items.Length >= 5",
            "목표 보유량",
            "hasGatherBatch",
        ):
            self.assertIn(token, source)

    def test_standalone_reuses_coordinator_and_keeps_single_input(self):
        source = read("source/FishingAutomation/MainForm.Gathering.cs")
        for token in (
            "QueuedGatheringPlans.ToArray()",
            "SelectedGatheringPlan()",
            "new MultiGatheringCoordinator",
            "new MultiGatheringRequest(x.DisplayName, x.TargetTotal)",
            "if (multiple)",
            "await coordinator.RunAsync(requests, token)",
            "await automation.RunAsync(plan, token)",
            "data.ItemCountAsync(item.DisplayName",
        ):
            self.assertIn(token, source)

    def test_coordinator_uses_same_verified_stop_fence(self):
        source = read("source/FishingAutomation/MultiGatheringCoordinator.cs")
        self.assertIn("new GatheringAutomation", source)
        self.assertIn("Math.Max(0, targetTotal - fresh)", source)
        self.assertIn("SourceRecipe is { }", source)
        self.assertNotIn("execute_gathering", source)
        self.assertNotIn("stop_action", source)

    def test_release_version(self):
        csproj = read("source/FishingAutomation/FishingAutomation.csproj")
        updater = read("source/FishingAutomation/UpdateManager.cs")
        if 'CurrentVersion = "V3.3.30"' in updater:
            self.assertIn("<Version>3.3.30</Version>", csproj)
        else:
            # The stock-target feature must also remain in newer releases.
            self.assertTrue(any(f'CurrentVersion = "V{v}"' in updater for v in ("3.3.31", "3.3.32", "3.3.33", "3.3.34", "3.3.35", "3.3.36", "3.3.37", "3.3.38", "3.3.39", "3.3.40", "3.3.41", "3.3.42", "3.3.43", "3.3.44", "3.3.45", "3.3.46", "3.3.47", "3.3.48", "3.3.49", "3.3.50", "3.3.51")))
            self.assertTrue(any(f"<Version>{v}</Version>" in csproj for v in ("3.3.31", "3.3.32", "3.3.33", "3.3.34", "3.3.35", "3.3.36", "3.3.37", "3.3.38", "3.3.39", "3.3.40", "3.3.41", "3.3.42", "3.3.43", "3.3.44", "3.3.45", "3.3.46", "3.3.47", "3.3.48", "3.3.49", "3.3.50", "3.3.51")))
            self.assertIn("test_v3330_gathering_stock_queue.py",
                          read(".github/workflows/release-v3331-auto-update.yml"))


if __name__ == "__main__":
    unittest.main()
