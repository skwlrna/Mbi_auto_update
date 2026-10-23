"""Safe one-shot reanalysis of Codex no-patch results.

This suite performs no real Codex, Git, network, PR, game or deployment actions.
"""
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
spec = importlib.util.spec_from_file_location("no_patch_reanalysis_agent", AI / "autopilot.py")
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class EndPoll(Exception):
    pass


class ReviewRetryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        root = Path(self.temp.name)
        self.reports = root / "reports"
        self.results = root / "results"
        self.reports.mkdir()
        self.results.mkdir()
        self.folder = self.reports / "runtime_20261024_033408_no_patch"
        self.folder.mkdir()
        evidence = self.folder / "error.txt"
        evidence.write_text("Altering move did not proceed on a verified facility UI", encoding="utf-8")
        old = time.time() - 90
        os.utime(evidence, (old, old))
        os.utime(self.folder, (old, old))
        self.eid = agent.event_id(self.folder)
        self.original = self.results / self.eid / "result.json"
        self.original.parent.mkdir()
        self.payload = {"status": "needs_review", "event": self.folder.name,
                        "event_id": self.eid, "missing_patch": True,
                        "reason": "agent_no_patch; analysis_no_patch; see codex-last-1.md"}
        agent.write_json(self.original, self.payload)
        agent.write_json(self.results / "watch-state.json",
                         {"seen": [self.eid], "processed": []})
        self.config = agent.config()
        self.config.update({"reports_dir": str(self.reports),
                            "runtime_reports_dir": str(root / "missing"),
                            "results_dir": str(self.results),
                            "settle_seconds": 1, "poll_seconds": 10})

    def cycle(self, handler):
        with mock.patch.object(agent, "require_isolated_ai_mode"), \
             mock.patch.object(agent, "process", side_effect=handler) as called, \
             mock.patch.object(agent.time, "sleep", side_effect=EndPoll):
            with self.assertRaises(EndPoll):
                agent.poll(self.config)
            return called

    def test_only_actual_no_patch_review_is_eligible(self):
        self.assertTrue(agent.review_retry_eligible(self.payload, self.eid, self.folder.name))
        for change in ({"missing_patch": False}, {"missing_patch": "true"},
                       {"status": "tests_passed_review_required"},
                       {"status": "tests_failed"}, {"status": "blocked"},
                       {"reason": "regression_test_missing"},
                       {"event_id": "f" * 16}, {"event": "different"}):
            with self.subTest(change=change):
                self.assertFalse(agent.review_retry_eligible(
                    {**self.payload, **change}, self.eid, self.folder.name))
        self.assertEqual(agent.review_retry_id(self.eid), self.eid + "-retry-review-v1")

    def test_never_automatically_replay_review(self):
        called = self.cycle(lambda *_args, **_kwargs: self.fail("unexpected Codex invocation"))
        called.assert_not_called()
        self.assertEqual(agent.read_json(self.original), self.payload)

    def test_explicit_request_runs_once_and_preserves_original(self):
        request = self.results / "review-retry-request.json"
        agent.write_json(request, {"action": "retry_no_patch_once", "event_id": self.eid})
        def handler(folder, config, **kwargs):
            self.assertEqual(folder, self.folder)
            self.assertTrue(kwargs["retry_review"])
            self.assertEqual(agent.read_json(self.original), self.payload)
            attempts = agent.read_json(self.results / "watch-state.json")["review_retry_attempted"]
            self.assertEqual(attempts, [self.eid])
            return {"status": "needs_review", "reason": "test_only"}
        called = self.cycle(handler)
        called.assert_called_once()
        self.assertFalse(request.exists())
        self.assertEqual(agent.read_json(self.original), self.payload)
        # Even a duplicated UI request is suppressed by the persisted receipt.
        agent.write_json(request, {"action": "retry_no_patch_once", "event_id": self.eid})
        self.cycle(lambda *_a, **_kw: self.fail("second Codex call")).assert_not_called()

    def test_invalid_or_ineligible_request_cannot_trigger_codex(self):
        request = self.results / "review-retry-request.json"
        for record in ({"action": "retry_no_patch_once", "event_id": "../bad"},
                       {"action": "retry_quota_blocked_once", "event_id": self.eid},
                       {"action": "retry_no_patch_once", "event_id": "0" * 16}):
            with self.subTest(record=record):
                agent.write_json(request, record)
                self.cycle(lambda *_a, **_kw: self.fail("untrusted request")).assert_not_called()
        self.assertEqual(agent.read_json(self.original), self.payload)

    def test_navigation_and_ui_do_not_claim_verified_fix(self):
        hints = agent.trusted_source_hints("다중가공 금속 가공 설비 이동 좌표 클릭 실패")
        self.assertEqual(len(hints), 3)
        self.assertIn("AlteringScreen.cs", hints[0])
        self.assertIn("altering-screen-integration/Program.cs", hints[1])
        ui = (ROOT / "source/FishingAutomation/AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        self.assertIn("RequestNoPatchRetry()", ui)
        self.assertIn('"retry_no_patch_once"', ui)
        self.assertIn('MessageBoxButtons.YesNo', ui)
        self.assertIn('missing.ValueKind != JsonValueKind.True', ui)
        self.assertNotIn("Game verified", agent.review_retry_id(self.eid))


if __name__ == "__main__":
    unittest.main()
