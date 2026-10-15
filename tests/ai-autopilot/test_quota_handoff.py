"""Offline quota pause, durable queue, and user-initiated ChatGPT handoff.

No live Codex, network, game input, automatic upload or deployment.
"""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest import mock

SCRIPT = Path(__file__).resolve().parents[2] / "source" / "FishingAutomation" / "ai-autopilot" / "autopilot.py"
SPEC = importlib.util.spec_from_file_location("mabiauto_quota_under_test", SCRIPT)
assert SPEC and SPEC.loader
agent = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(agent)
from quota_handoff import exhausted, handoff_manifest, pause_record, resume_authorized


class StopAfterCycle(Exception):
    pass


class QuotaFallbackTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        self.reports = root / "reports"
        self.results = root / "results"
        self.reports.mkdir()
        self.results.mkdir()
        self.c = agent.config()
        self.c.update({"reports_dir": str(self.reports),
                       "results_dir": str(self.results),
                       "settle_seconds": 1, "poll_seconds": 10,
                       "max_runs_per_day": 8})
        agent.write_json(self.results / "watch-state.json", {"seen": [], "processed": []})

    def error(self, name, content, age=100):
        folder = self.reports / name
        folder.mkdir()
        f = folder / "error.txt"
        f.write_text(content, encoding="utf-8")
        when = time.time() - age
        os.utime(f, (when, when))
        os.utime(folder, (when, when))
        return folder

    def cycle(self, action):
        with mock.patch.object(agent, "require_legacy_off"), \
             mock.patch.object(agent, "process", side_effect=action) as invoked, \
             mock.patch.object(agent.time, "sleep", side_effect=StopAfterCycle):
            with self.assertRaises(StopAfterCycle):
                agent.poll(self.c)
        return invoked

    def test_explicit_quota_words_not_generic_429(self):
        self.assertTrue(exhausted("You've hit your usage limit. Try again later"))
        self.assertTrue(exhausted("error: insufficient_quota"))
        self.assertTrue(exhausted("Codex usage limit reached"))
        self.assertFalse(exhausted("429 Too Many Requests: retry later"))
        self.assertFalse(exhausted("503 Service Unavailable"))

    def test_quota_ends_codex_retries_immediately(self):
        with mock.patch.object(agent.shutil, "which", return_value="codex"), \
             mock.patch.object(agent, "require_legacy_off"), \
             mock.patch.object(agent, "supervised_run", return_value=(
                 1, '{"type":"turn.failed","error":{"message":"usage limit reached"}}',
                 "You've hit your usage limit", None)) as call, \
             mock.patch.object(agent.time, "sleep") as sleep:
            ok, reason = agent.exec_codex(self.results, "test", self.results, self.c)
        self.assertFalse(ok)
        self.assertEqual(reason, "codex_quota_exhausted")
        self.assertEqual(call.call_count, 1)
        sleep.assert_not_called()

    def test_quota_is_persistent_and_new_error_waits_without_codex(self):
        original = self.error("error_first", "scheduler stalled SECRET-DO-NOT-SHARE", 120)
        first = self.cycle(lambda *a, **k: {"status": "blocked",
                                          "reason": "codex_unverified: codex_quota_exhausted"})
        self.assertEqual(first.call_count, 1)
        state = agent.read_json(self.results / "watch-state.json")
        self.assertEqual(state["quota_pause"]["reason"], "codex_quota_exhausted")
        self.assertIn(agent.event_id(original), state["seen"])
        self.assertEqual(len(state["handoff_events"]), 1)
        next_error = self.error("error_second", "A separate new scheduler issue", 60)
        again = self.cycle(lambda *a, **k: self.fail("Quota pause called Codex"))
        again.assert_not_called()
        after = agent.read_json(self.results / "watch-state.json")
        self.assertNotIn(agent.event_id(next_error), after["seen"])
        activity = agent.read_json(self.results / "watch-activity.json")
        self.assertEqual(activity["status"], "quota_paused")
        self.assertEqual(activity["pending"], 1)
        handoff = (self.results / "chatgpt-handoff.json").read_text(encoding="utf-8")
        self.assertIn(agent.event_id(original), handoff)
        self.assertIn(agent.event_id(next_error), handoff)
        self.assertNotIn("SECRET-DO-NOT-SHARE", handoff)
        self.assertNotIn(str(original), handoff)
        self.assertFalse(json.loads(handoff)["automatic_chatgpt_upload"])

    def test_resume_requires_matching_pause_id_and_user_request(self):
        first = self.error("error_quota", "first error distinct enough", 120)
        self.cycle(lambda *a, **k: {"status": "blocked",
                                    "reason": "codex_unverified: codex_quota_exhausted"})
        pause = agent.read_json(self.results / "watch-state.json")["quota_pause"]
        self.error("error_unprocessed", "later unrelated receipt error", 65)
        requestfile = self.results / "quota-resume-request.json"
        agent.write_json(requestfile, {"action":"retry_after_user_confirmed_reset",
                                      "pause_id": "0"*32})
        self.cycle(lambda *a, **k: self.fail("invalid reset tried Codex"))
        self.assertIn("quota_pause", agent.read_json(self.results / "watch-state.json"))
        agent.write_json(requestfile, {"action":"retry_after_user_confirmed_reset",
                                      "pause_id": pause["id"]})
        calls = self.cycle(lambda *a, **k: {"status": "blocked", "reason": "test_nonquota"})
        self.assertEqual(calls.call_count, 1)
        self.assertFalse(requestfile.exists())
        state = agent.read_json(self.results / "watch-state.json")
        self.assertNotIn("quota_pause", state)
        self.assertIn(agent.event_id(first), state["seen"])  # no automatic replay
        self.assertEqual(len(state["processed"]), 2)

    def test_handoff_never_includes_untrusted_raw_text(self):
        folder = self.error("error_secret", "token=supersecret xxxxxxxxxxxx", 50)
        handoff = handoff_manifest([folder], "codex_quota_exhausted",
                                   agent.event_id, lambda _: "UNTRUSTED PRIVATE LOGS")
        content = json.dumps(handoff)
        self.assertNotIn("supersecret", content)
        self.assertNotIn(str(folder), content)
        self.assertNotIn("PRIVATE", content)
        self.assertEqual(handoff["items"][0]["suspected_category"], "unclassified")

    def test_resume_rejects_unknown_action_and_stale_pause_token(self):
        pause = pause_record()
        self.assertFalse(resume_authorized({"action": "retry_after_user_confirmed_reset",
                                           "pause_id": "a"*32}, pause))
        self.assertFalse(resume_authorized({"action": "resume_automatically",
                                           "pause_id": pause["id"]}, pause))
        self.assertTrue(resume_authorized({"action": "retry_after_user_confirmed_reset",
                                          "pause_id": pause["id"]}, pause))

    def test_duplicate_signature_persists_across_watch_restart(self):
        first = self.error("first", "Identical repeated scheduler error text much longer than 24", 120)
        result = self.cycle(lambda *a, **k: {"status":"blocked", "reason":"test"})
        self.assertEqual(result.call_count, 1)
        second = self.error("second", "Identical repeated scheduler error text much longer than 24", 90)
        dup = self.cycle(lambda *a, **k: self.fail("duplicate called Codex"))
        dup.assert_not_called()
        self.assertEqual(agent.read_json(self.results / "watch-state.json")["processed"][-1]["status"],
                         "duplicate_skipped")
        self.assertTrue((second / "error.txt").exists())

    def test_daily_cap_provides_manual_handoff_without_processing(self):
        state = agent.read_json(self.results / "watch-state.json")
        state["processed"] = [{"date":agent.dt.date.today().isoformat(),
                               "status":"blocked"}] * 8
        agent.write_json(self.results / "watch-state.json", state)
        folder = self.error("error_waiting", "scheduler error not yet processed", 60)
        no_call = self.cycle(lambda *a, **k: self.fail("daily cap called Codex"))
        no_call.assert_not_called()
        payload = agent.read_json(self.results / "chatgpt-handoff.json")
        self.assertEqual(payload["status"], "daily_limit")
        self.assertEqual(payload["items"][0]["event_id"], agent.event_id(folder))

    def test_dashboard_only_copies_and_requests_local_resume(self):
        ui = (SCRIPT.parents[1] / "AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        host = (SCRIPT.parents[1] / "AiAutopilotHost.cs").read_text(encoding="utf-8")
        self.assertIn("ChatGPT 인계 복사", ui)
        self.assertIn("Clipboard.SetText(prompt);", ui)
        self.assertIn("retry_after_user_confirmed_reset", ui)
        self.assertIn("quota_paused", ui)
        self.assertIn("Codex 사용량 소진", host)
        self.assertNotIn("GenUI.issueNewTurn", ui)
        self.assertNotIn("api.openai.com", ui)


if __name__ == "__main__":
    unittest.main()
