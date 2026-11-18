"""V3.3.34: fresh tannin 1275/4 should not block missing craftable leather."""
from pathlib import Path
import json
import unittest

ROOT = Path(__file__).resolve().parents[2]
def read(path): return (ROOT / path).read_text(encoding="utf-8")

class TanninInventoryV3334(unittest.TestCase):
    def test_sufficient_stock_is_only_read_only_route_evidence(self):
        route = read("source/FishingAutomation/MultiAlteringCoordinator.cs")
        core = read("source/FishingAutomation/AlteringPlan.cs")
        tests = read("tests/altering/Program.cs")
        self.assertLess(route.index("if (current >= required) return null"),
                        route.index('current != recipeOwned.Value)'))
        for token in ("verifiedSufficientRaw.Add(raw.DisplayName)",
                      "MissingIngredients = recipe.MissingIngredients",
                      "if (recipe.MissingIngredients.Count == 0)",
                      "same[plan.RecipeOrdinal - 1].Alterable",
                      "YieldManagedMaterialMismatchAsync(",
                      "IsolateEmptyMaterialHoldAsync(",
                      '"leather_recipe_inventory_mismatch"'):
            self.assertIn(token, core)
        for token in ("2026-11-18 tannin 1275/4", "tanninWithCraftableLeather",
                      "tanninWithMissingHide", "external_leather_raw_missing"):
            self.assertIn(token, tests)
        # Neither a route read nor stale Owned may authorize a registration.
        self.assertIn("if (selected.Length < plan.RecipeOrdinal ||", core)
        self.assertIn("!selected[plan.RecipeOrdinal - 1].Alterable", core)

    def test_version_and_ci_gated_publish(self):
        self.assertIn('CurrentVersion = "V3.3.34"', read("source/FishingAutomation/UpdateManager.cs"))
        self.assertIn("<Version>3.3.34</Version>", read("source/FishingAutomation/FishingAutomation.csproj"))
        a = json.loads(read("source/V3.3.34_RELEASE_AUTHORIZATION.json"))
        self.assertFalse(a["realGameTested"])
        self.assertTrue(a["mainCiRequired"])
        self.assertEqual(a["purpose"], "sufficient_tannin_stock_does_not_block_craftable_leather")
        workflow = read(".github/workflows/release-v3334-auto-update.yml")
        for marker in ("No successful production CI for current main SHA",
                       "gh release create V3.3.34",
                       "MabiAuto_V3.3.34_Windows_Lite.zip.sha256",
                       "test_v3334_tannin_inventory.py"):
            self.assertIn(marker, workflow)

if __name__ == "__main__":
    unittest.main()
