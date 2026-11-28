"""Keep mobile queue execution on the PC multi-item batch path."""
from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[2]
PHONE = ROOT / "source/FishingAutomation/MainForm.PhoneAltering.cs"


class MobileAlteringBatch(unittest.TestCase):
    def test_pending_plans_go_to_one_multi_altering_call(self):
        source = PHONE.read_text(encoding="utf-8")
        self.assertIn('Orders.Where(order => order.State == "시작 대기").ToArray()', source)
        self.assertIn("plans[order.Name] = plan;", source)
        self.assertIn("StartMultiAlteringAsync(plans.Values.ToArray(), remoteOrder: true,", source)
        self.assertEqual(source.count("StartMultiAlteringAsync("), 1)
        self.assertNotIn("StartMultiAlteringAsync([plan]", source)

    def test_stock_and_prior_requests_adjust_total_targets(self):
        source = PHONE.read_text(encoding="utf-8")
        self.assertIn("order.Quantity - stock[recipe.OutputName] - planned", source)
        self.assertIn("TargetQuantity = checked(planned + amount)", source)
        self.assertIn('State = completed || confirmedOrders.Contains(order.Number) ? "완료" : "확인 필요"', source)
