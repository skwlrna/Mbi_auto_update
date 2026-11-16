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
        self.assertIn("<Version>3.3.30</Version>", csproj)
        self.assertIn('CurrentVersion = "V3.3.30"', updater)


if __name__ == "__main__":
    unittest.main()
