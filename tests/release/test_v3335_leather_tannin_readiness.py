"""V3.3.35: sufficient tannin must not mask another shortage or grant input."""
from pathlib import Path
import json
import unittest
ROOT = Path(__file__).resolve().parents[2]
def read(path): return (ROOT / path).read_text(encoding="utf-8")

class TanninRecipeReadiness(unittest.TestCase):
    def test_sufficient_stock_is_deferred_but_not_auto_authorized(self):
        manager = read("source/FishingAutomation/MainForm.MultiAltering.cs")
        route = read("source/FishingAutomation/MultiAlteringCoordinator.cs")
        automation = read("source/FishingAutomation/AlteringPlan.cs")
        regression = read("tests/altering/Program.cs")
        self.assertIn("MaterialSupplyHold? sufficientRawConflict = null;", route)
        self.assertIn("if (current < required)", route)
        self.assertIn("return sufficientRawConflict;", route)
        self.assertIn("leather-sufficient-stock-recipe-recheck", manager)
        self.assertIn("if (latest.Alterable)", manager)
        self.assertIn("Alterable={latest.Alterable}", manager)
        self.assertIn("hasSufficientRawConflict", automation)
        self.assertIn("if (rechecked[plan.RecipeOrdinal - 1].Alterable)", automation)
        self.assertIn("실재고 충분만으로 제작 등록하지 않음", automation)
        self.assertIn("tanninDeferredHold.Item == \"생가죽\"", regression)
        self.assertIn("tanninOnlyHold.Observed == 1275", regression)

    def test_release_is_authorized_and_rollback_gated(self):
        a = json.loads(read("source/V3.3.35_RELEASE_AUTHORIZATION.json"))
        self.assertEqual(a["version"], "V3.3.35")
        self.assertEqual(a["purpose"], "leather_tannin_fresh_recipe_readiness_reconciliation")
        self.assertFalse(a["realGameTested"])
        self.assertTrue(a["rollbackProvided"] and a["mainCiRequired"])
        self.assertRegex(
                      read("source/FishingAutomation/UpdateManager.cs"), r'CurrentVersion = "V3\.3\.(35|36|37|38|39|40|41|42|43|44|45|46|47|48)"')
        self.assertRegex(
                      read("source/FishingAutomation/FishingAutomation.csproj"), r"<Version>3\.3\.(35|36|37|38|39|40|41|42|43|44|45|46|47|48)</Version>")
        workflow = read(".github/workflows/release-v3335-auto-update.yml")
        for key in ("No successful production CI for current main SHA",
                    "gh release create V3.3.35",
                    "MabiAuto_V3.3.35_Windows_Lite.zip.sha256",
                    "test_v3335_leather_tannin_readiness.py"):
            self.assertIn(key, workflow)

if __name__ == "__main__": unittest.main()
