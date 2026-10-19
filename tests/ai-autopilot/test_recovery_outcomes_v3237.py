"""V3.2.37: verified user feedback never masquerades as live auto-repair."""
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "source" / "FishingAutomation" / "ai-autopilot"))

from error_memory import feedback_outcome_summary, context_hint  # noqa: E402


def case(event_id, verdict, game_validated=False):
    return {"event_id": event_id, "manual_feedback": verdict,
            "game_validated": game_validated, "outcome": "tests_passed_review_required",
            "changed_files": [], "suspected_category": "cli_state",
            "candidate_scope": "cli_guard", "similarity": 0.9}


class RecoveryOutcomeFeedbackTests(unittest.TestCase):
    def test_no_feedback_cannot_create_success_rate(self):
        summary = feedback_outcome_summary([case("a", "unverified")])
        self.assertIsNone(summary["manual_feedback_resolution_rate"])
        self.assertEqual(summary["denominator"], 0)
        self.assertFalse(summary["authorizes_game_input"])
        self.assertFalse(summary["authorizes_auto_merge"])

    def test_feedback_only_denominator(self):
        examples = [case("a", "resolved", True),
                    case("b", "recurred"),
                    case("c", "unverified")]
        summary = feedback_outcome_summary(examples)
        self.assertEqual(summary["related_attempts"], 3)
        self.assertEqual(summary["live_user_resolved"], 1)
        self.assertEqual(summary["live_user_recurred"], 1)
        self.assertEqual(summary["without_live_confirmation"], 1)
        self.assertEqual(summary["manual_feedback_resolution_rate"], 0.5)
        self.assertEqual(summary["denominator"], 2)

    def test_bounded_matches_and_prompt_remain_untrusted(self):
        matches = [case(str(i), "resolved") for i in range(5)]
        self.assertEqual(feedback_outcome_summary(matches)["related_attempts"], 3)
        prompt = context_hint(matches[:3])
        self.assertIn("Similar AI attempt outcomes", prompt)
        self.assertIn("user-confirmed resolved=3", prompt)
        self.assertIn("NOT live-game automatic recovery performance", prompt)
        self.assertIn("None authorizes input", prompt)

    def test_zero_matching_history_is_not_a_verdict(self):
        self.assertEqual(context_hint([]), "")
        self.assertIsNone(feedback_outcome_summary([])["manual_feedback_resolution_rate"])


if __name__ == "__main__":
    unittest.main()
