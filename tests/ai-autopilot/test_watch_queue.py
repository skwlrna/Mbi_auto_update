"""Offline, deterministic regression checks for the MabiAuto MCP watch queue.

These tests never launch Codex, the game, Git commands or the updater.
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
SPEC = importlib.util.spec_from_file_location("mabiauto_autopilot_under_test", SCRIPT)
assert SPEC and SPEC.loader
agent = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(agent)


class BreakAfterPolling(Exception):
    pass


class QueueTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        root = Path(self.tmp.name)
        self.reports = root / "reports"
        self.results = root / "results"
        self.reports.mkdir()
        self.results.mkdir()
        self.config = agent.config()
        self.config.update({
            "reports_dir": str(self.reports), "results_dir": str(self.results),
            "settle_seconds": 1
        })
        (self.results / "watch-state.json").write_text(
            json.dumps({"seen": [], "processed": []}), encoding="utf-8"
        )

    def error(self, name, evidence, age=90):
        folder = self.reports / name
        folder.mkdir()
        file = folder / "error.txt"
        file.write_text(evidence, encoding="utf-8")
        timestamp = time.time() - age
        os.utime(file, (timestamp, timestamp))
        os.utime(folder, (timestamp, timestamp))
        return folder

    def run_one_cycle(self):
        events = []
        with mock.patch.object(agent, "require_legacy_off"), \
             mock.patch.object(agent, "process", side_effect=lambda p, c, **k:
                               events.append(p.name) or {"status":"blocked", "reason":"unit-test"}), \
             mock.patch.object(agent.time, "sleep", side_effect=BreakAfterPolling):
            with self.assertRaises(BreakAfterPolling):
                agent.poll(self.config)
        return events

    def test_newest_first_not_oldest_first(self):
        self.error("older", "old event carries unique diagnostic text", 180)
        self.error("newer", "new event carries unique diagnostic text", 60)
        self.assertEqual(self.run_one_cycle(), ["newer", "older"])
        state = agent.read_json(self.results / "watch-state.json")
        self.assertEqual(len(state["processed"]), 2)

    def test_identical_evidence_deduplicates_without_dropping_report(self):
        evidence = "Same exact recovered error evidence 22:55"
        self.error("older", evidence, 180)
        self.error("newer", evidence, 60)
        self.assertEqual(self.run_one_cycle(), ["newer"])
        state = agent.read_json(self.results / "watch-state.json")
        statuses = sorted(x["status"] for x in state["processed"])
        self.assertEqual(statuses, ["blocked", "duplicate_skipped"])
        older_id = agent.event_id(self.reports / "older")
        self.assertTrue((self.results / older_id / "result.json").is_file())
        self.assertTrue((self.reports / "older" / "error.txt").is_file())

    def test_distinct_error_evidence_is_not_deduplicated(self):
        self.error("older", "wood processing facility did not transition", 180)
        self.error("newer", "K field independent HUD confirmation failed", 60)
        self.assertEqual(len(self.run_one_cycle()), 2)

    def test_local_daily_count_no_longer_blocks_provider_usage(self):
        statefile = self.results / "watch-state.json"
        state = agent.read_json(statefile)
        state["processed"] = [{"date":agent.dt.date.today().isoformat(), "status":"blocked"}] * 80
        agent.write_json(statefile, state)
        self.error("pending", "new unique facility error never ignored", 60)
        self.assertEqual(self.run_one_cycle(), ["pending"])
        status = agent.read_json(self.results / "watch-activity.json")
        self.assertNotEqual(status["status"], "daily_limit")
        self.assertEqual(status["pending"], 0)

    def test_processing_status_written_before_codex_work(self):
        self.error("new", "unique failure full evidence for real event", 60)
        def process_and_check(p, c, **kwargs):
            self.assertEqual(agent.read_json(self.results / "watch-activity.json")["status"], "processing")
            return {"status":"blocked", "reason":"test"}
        with mock.patch.object(agent, "require_legacy_off"), \
             mock.patch.object(agent, "process", side_effect=process_and_check), \
             mock.patch.object(agent.time, "sleep", side_effect=BreakAfterPolling):
            with self.assertRaises(BreakAfterPolling):
                agent.poll(self.config)
        self.assertEqual(agent.read_json(self.results / "watch-activity.json")["status"], "watching")


if __name__ == "__main__":
    unittest.main()
