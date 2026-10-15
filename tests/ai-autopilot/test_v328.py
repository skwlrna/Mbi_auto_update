"""V3.2.8: bounded local AI incident memory and F9 preflight contract."""
from pathlib import Path
import hashlib
import json
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import error_memory


def evidence(replace: str = "금속 가공 수령 후 완료창 헤더 겹침") -> str:
    return (
        "MCP error event. CLI receives finished products, but the facility "
        "header and reward result dialog remain ambiguous at the same time. "
        "Read-only post-receipt inspection reports activity, field position, "
        "current metal facility and processing result. " + replace
    )


class ErrorMemoryTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    def test_only_hashes_and_allowlisted_candidate_paths_are_saved(self):
        secret = "raw-private-log-never-store abcXYZ" + "CONFIDENTIAL"
        context = evidence(secret)
        error_memory.record_attempt(self.root, "0123456789abcdef", context, {
            "status": "tests_passed_review_required",
            "changed_files": ["source/FishingAutomation/AlteringScreen.cs",
                              "tests/altering/Program.cs",
                              "../outside/path", "notes/custom.txt"]
        })
        content = (self.root / "error-memory.json").read_text(encoding="utf-8")
        self.assertNotIn(secret, content)
        self.assertNotIn("금속", content)
        self.assertNotIn("../outside", content)
        self.assertIn("game_validated", content)
        self.assertIn('"game_validated": false', content)
        self.assertIn("tests_passed_review_required", content)

    def test_similar_errors_recall_unverified_candidate_only(self):
        x = evidence()
        error_memory.record_attempt(self.root, "0123456789abcdef", x, {
            "status": "tests_passed_review_required",
            "changed_files": ["source/FishingAutomation/AlteringScreen.cs"]
        })
        related = error_memory.related_attempts(self.root, x + " 금속 수령", exclude="othercase")
        self.assertEqual(len(related), 1)
        self.assertEqual(related[0]["game_validated"], False)
        note = error_memory.context_hint(related)
        self.assertIn("NOT commands", note)
        self.assertIn("none proved live-game repair", note)
        self.assertNotIn("<system>", note)

    def test_missing_history_never_blocks_new_error(self):
        self.assertEqual(error_memory.related_attempts(self.root, evidence()), [])
        error_memory.record_attempt(self.root, "not-sanitized", evidence(), {
            "status": "tests_passed_review_required"
        })
        self.assertFalse((self.root / "error-memory.json").exists())

    def test_history_rotation_and_dedup(self):
        text = evidence()
        for i in range(90):
            error_memory.record_attempt(self.root, f"{i:016x}", text, {
                "status": "needs_review", "changed_files": []
            })
        memory = json.loads((self.root / "error-memory.json").read_text(encoding="utf-8"))
        self.assertEqual(len(memory["entries"]), 80)
        self.assertEqual(memory["entries"][0]["event_id"], f"{89:016x}")
        error_memory.record_attempt(self.root, f"{89:016x}", text, {
            "status": "tests_failed"
        })
        after = json.loads((self.root / "error-memory.json").read_text(encoding="utf-8"))
        self.assertEqual(len(after["entries"]), 80)
        self.assertEqual(after["entries"][0]["outcome"], "tests_failed")

    def test_irrelevant_event_is_not_presented_as_solution(self):
        x = evidence()
        error_memory.record_attempt(self.root, "abcdef0123456789", x, {
            "status": "blocked", "changed_files": []
        })
        related = error_memory.related_attempts(
            self.root, "Fishing aquarium or pet recruitment unrelated mechanic settings")
        self.assertEqual(related, [])

    def test_preflight_invoked_before_macro_dispatch(self):
        main = (ROOT / "source" / "FishingAutomation" / "MainForm.cs").read_text(encoding="utf-8")
        diagnostics = (ROOT / "source" / "FishingAutomation" /
                       "MainForm.Diagnostics.cs").read_text(encoding="utf-8")
        self.assertLess(main.index("if (!RunAutomationStartPreflight(mode)) return;"),
                        main.index('if (mode == "낚시") StartFishing();'))
        self.assertIn("AutomationStartPreflightPolicy.Evaluate(", diagnostics)
        self.assertIn("[시작 전 자동점검]", diagnostics)
        self.assertIn('if (mode == "스케줄러")', diagnostics)

    def test_autopilot_memory_never_restarts_crashed_work(self):
        agent = (AI / "autopilot.py").read_text(encoding="utf-8")
        self.assertIn("from error_memory import record_attempt, related_attempts, context_hint", agent)
        self.assertIn("memory_context = context", agent)
        self.assertIn("return read_json(result_path)", agent)
        self.assertIn("record_attempt(expand(str(c", agent)


if __name__ == "__main__":
    unittest.main()
