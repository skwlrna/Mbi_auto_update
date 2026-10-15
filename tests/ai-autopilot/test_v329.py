"""V3.2.9: structured AI memory, explicit user feedback and failed-patch veto."""
import json
import sys
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
import error_memory

EVIDENCE = ("MCP error event. CLI work count mismatch after metal receipt and result "
            "overlay. OCR recognizes an old facility header while the game is idle. "
            "Read-only process evidence shows unverified completion. " * 2)
EVENT = "0123456789abcdef"
EVENT2 = "fedcba9876543210"
PATHS = ["source/FishingAutomation/dungeon/AlteringScreen.cs", "tests/altering/Program.cs"]


class StructuredLearningTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)

    def save_result(self, eid=EVENT, status="tests_passed_review_required"):
        folder = self.root / eid
        folder.mkdir(parents=True, exist_ok=True)
        (folder / "result.json").write_text(
            json.dumps({"event_id": eid, "status": status}), encoding="utf-8")
        return folder

    def feedback(self, verdict, eid=EVENT, source="manual_ui"):
        folder = self.root / eid
        folder.mkdir(parents=True, exist_ok=True)
        (folder / "user-feedback.json").write_text(
            json.dumps({"event_id": eid, "source": source,
                        "verdict": verdict, "confirmed_at": "2026-10-15T00:00:00Z"}),
            encoding="utf-8")

    def test_feedback_needs_human_source_and_test_passed_result(self):
        self.save_result()
        self.assertEqual(error_memory.read_user_feedback(self.root, EVENT), "unverified")
        self.feedback("resolved", source="automated_test")
        self.assertEqual(error_memory.read_user_feedback(self.root, EVENT), "unverified")
        self.feedback("resolved")
        self.assertEqual(error_memory.read_user_feedback(self.root, EVENT), "resolved")
        self.save_result(status="tests_failed")
        self.assertEqual(error_memory.read_user_feedback(self.root, EVENT), "unverified")

    def test_explicit_feedback_survives_lookup_without_false_auto_success(self):
        self.save_result()
        error_memory.record_attempt(self.root, EVENT, EVIDENCE, {
            "status": "tests_passed_review_required",
            "changed_files": PATHS
        })
        self.assertEqual(error_memory.related_attempts(self.root, EVIDENCE)[0]["game_validated"], False)
        self.feedback("resolved")
        r = error_memory.related_attempts(self.root, EVIDENCE)[0]
        self.assertTrue(r["game_validated"])
        self.assertEqual(r["manual_feedback"], "resolved")
        note = error_memory.context_hint([r])
        self.assertIn("USER MARKED FIX RESOLVED", note)
        self.feedback("recurred")
        r = error_memory.related_attempts(self.root, EVIDENCE)[0]
        self.assertFalse(r["game_validated"])
        self.assertEqual(r["manual_feedback"], "recurred")
        self.assertIn("REPORTED RECURRENCE", error_memory.context_hint([r]))
        self.feedback("unverified")
        self.assertEqual(error_memory.related_attempts(self.root, EVIDENCE)[0]["manual_feedback"],
                         "unverified")

    def test_classification_is_heuristic_enum_not_claim_of_proven_cause(self):
        info = error_memory.classify(EVIDENCE, PATHS)
        self.assertIn(info["suspected_category"], error_memory.CATEGORIES)
        self.assertIn(info["candidate_scope"], error_memory.METHOD_SCOPES)
        self.assertEqual(info["cause_confidence"], "heuristic_not_verified")
        error_memory.record_attempt(self.root, EVENT, EVIDENCE, {
            "status": "needs_review", "changed_files": PATHS
        })
        history = json.loads((self.root / "error-memory.json").read_text(encoding="utf-8"))
        entry = history["entries"][0]
        self.assertEqual(entry["classification"]["cause_confidence"], "heuristic_not_verified")
        self.assertNotIn("overlay", json.dumps(entry))

    def test_exact_failed_candidate_fingerprint_blocks_reuse(self):
        tree = self.root / "worktree"
        for i, path in enumerate(PATHS):
            file = tree / path
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text("test patch line = " + str(i), encoding="utf-8")
        fp = error_memory.patch_fingerprint(tree, PATHS)
        self.assertEqual(len(fp), 64)
        error_memory.record_attempt(self.root, EVENT, EVIDENCE, {
            "status": "tests_failed", "changed_files": PATHS,
            "patch_fingerprint": fp
        })
        self.assertTrue(error_memory.failed_fingerprint(self.root, fp))
        self.assertFalse(error_memory.failed_fingerprint(self.root, "f" * 64))
        (tree / PATHS[0]).write_text("meaningfully changed patch", encoding="utf-8")
        self.assertNotEqual(error_memory.patch_fingerprint(tree, PATHS), fp)

    def test_manual_recurrence_blocks_exact_patch_but_not_unknown_fix(self):
        self.save_result()
        fp = "b" * 64
        error_memory.record_attempt(self.root, EVENT, EVIDENCE, {
            "status": "tests_passed_review_required", "changed_files": PATHS,
            "patch_fingerprint": fp
        })
        self.assertFalse(error_memory.failed_fingerprint(self.root, fp))
        self.feedback("recurred")
        self.assertTrue(error_memory.failed_fingerprint(self.root, fp))
        self.feedback("resolved")
        self.assertFalse(error_memory.failed_fingerprint(self.root, fp))

    def test_dashboard_is_manual_and_no_auto_merge(self):
        view = (ROOT / "source" / "FishingAutomation" /
                "AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        agent = (AI / "autopilot.py").read_text(encoding="utf-8")
        self.assertIn('SubmitGameFeedback("resolved")', view)
        self.assertIn('SubmitGameFeedback("recurred")', view)
        self.assertIn('SubmitGameFeedback("unverified")', view)
        self.assertIn('source = "manual_ui"', view)
        self.assertIn('File.Move(temp, target, true)', view)
        self.assertIn('failed_fingerprint(', agent)
        self.assertIn('known_failed_candidate_exact_match_no_auto_reuse', agent)
        self.assertIn('return finish("needs_review", "known_failed_candidate_after_repair_no_auto_reuse")', agent)
        self.assertNotIn("gh pr merge", view)

    def test_invalid_feedback_or_untrusted_paths_do_not_leak_into_memory(self):
        self.save_result()
        self.feedback("resolved", source="not_a_human_ui")
        self.assertEqual(error_memory.read_user_feedback(self.root, EVENT), "unverified")
        data = EVIDENCE + " PERSONAL-PRIVATE-TOKEN-NEVER-PERSIST"
        error_memory.record_attempt(self.root, EVENT, data, {
            "status": "tests_passed_review_required",
            "changed_files": ["../secret", "source/FishingAutomation/MainForm.cs"]
        })
        text = (self.root / "error-memory.json").read_text(encoding="utf-8")
        self.assertNotIn("PERSONAL-PRIVATE-TOKEN", text)
        self.assertNotIn("../secret", text)
        self.assertIn("classification", text)


if __name__ == "__main__":
    unittest.main()
