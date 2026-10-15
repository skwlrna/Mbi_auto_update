"""Offline checks for the integrated runtime-error -> Codex queue -> human timeline.

Does not launch Codex, the game, a Windows UI, network access, or Git commands.
"""
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import time
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "source" / "FishingAutomation"
SCRIPT = APP / "ai-autopilot" / "autopilot.py"
SPEC = importlib.util.spec_from_file_location("mabiauto_timeline_autopilot", SCRIPT)
assert SPEC and SPEC.loader
agent = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(agent)


class TimelineTests(unittest.TestCase):
    def test_autopilot_no_longer_requires_legacy_autofix(self):
        source = SCRIPT.read_text(encoding="utf-8")
        config = json.loads((APP / "ai-autopilot" / "config.json").read_text(encoding="utf-8"))
        self.assertNotIn("require_autofix_off", config)
        self.assertNotIn("legacy_autofix_guard", source)
        self.assertNotIn("tools/codex-autofix", source)
        self.assertIn("require_isolated_ai_mode", source)
        self.assertIn("SingletonLock", source)
        self.assertIn("require_clean_repo", source)

    def test_local_runtime_report_is_scanned_with_mcp_without_duplicate(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            mcp = root / "MCP"
            local = root / "runtime-reports"
            for p in (mcp, local):
                p.mkdir()
            old = mcp / "error_old"
            new = local / "runtime_20261016_051737_000_abcd1234"
            old.mkdir()
            new.mkdir()
            (old / "error.txt").write_text("MCP error evidence from previous cycle", encoding="utf-8")
            (new / "summary.json").write_text(json.dumps({
                "timeLocal": "2026-10-16T05:17:37+09:00",
                "title": "다중가공 오류",
                "detail": "생활 스킬 채집 중 진행 불가",
            }, ensure_ascii=False), encoding="utf-8")
            (new / "error.txt").write_text("채집 중 정지 - no screenshots", encoding="utf-8")
            earlier = time.time() - 160
            later = time.time() - 70
            for file in old.iterdir():
                os.utime(file, (earlier, earlier))
            for file in new.iterdir():
                os.utime(file, (later, later))
            os.utime(old, (earlier, earlier))
            os.utime(new, (later, later))
            config = {"reports_dir": str(mcp), "runtime_reports_dir": str(local)}
            names = [p.name for p in agent.all_evidence_folders(config)]
            self.assertEqual(names, [new.name, old.name])
            self.assertTrue(agent.stable(new, 20))
            self.assertEqual(len(agent.all_evidence_folders(config)), 2)

    def test_missing_external_mcp_folder_does_not_hide_runtime_error(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            local = root / "runtime-reports"
            local.mkdir()
            report = local / "runtime_20261016_000111_000_abcd1234"
            report.mkdir()
            (report / "error.txt").write_text("어비스 나가기 실패", encoding="utf-8")
            found = agent.all_evidence_folders({
                "reports_dir": str(root / "missing-mcp"),
                "runtime_reports_dir": str(local),
            })
            self.assertEqual([p.name for p in found], [report.name])

    def test_first_start_processes_local_runtime_errors_not_legacy_mcp(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            mcp, local, results = (root / "reports", root / "runtime-reports", root / "results")
            for directory in (mcp, local, results):
                directory.mkdir()
            existing_mcp = mcp / "error_old"
            retained_runtime = local / "runtime_20261016_051737_000_abcd1234"
            for directory, content in (
                (existing_mcp, "old optional MCP report"),
                (retained_runtime, "new app runtime failure"),
            ):
                directory.mkdir()
                evidence = directory / "error.txt"
                evidence.write_text(content, encoding="utf-8")
                before = time.time() - 90
                os.utime(evidence, (before, before))
                os.utime(directory, (before, before))

            config = agent.config()
            config.update({
                "reports_dir": str(mcp), "runtime_reports_dir": str(local),
                "results_dir": str(results), "settle_seconds": 1,
            })
            processed = []
            def record(folder, *_args, **_kwargs):
                processed.append(folder.name)
                return {"status": "blocked", "reason": "offline-test"}

            class EndCycle(Exception):
                pass

            with mock.patch.object(agent, "require_isolated_ai_mode"), \
                 mock.patch.object(agent, "process", side_effect=record), \
                 mock.patch.object(agent.time, "sleep", side_effect=EndCycle):
                with self.assertRaises(EndCycle):
                    agent.poll(config)
            self.assertEqual(processed, [retained_runtime.name])

    def test_ui_shows_time_specific_errors_and_next_step(self):
        dashboard = (APP / "AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        journal = (APP / "AiRuntimeErrorJournal.cs").read_text(encoding="utf-8")
        uploader = (APP / "RuntimeErrorUploader.cs").read_text(encoding="utf-8")
        self.assertIn('yyyy-MM-dd HH:mm:ss', dashboard)
        self.assertIn('"어떤 오류?"', dashboard)
        self.assertIn('"상세 내용"', dashboard)
        self.assertIn('"AI가 한 일"', dashboard)
        self.assertIn('"다음 단계"', dashboard)
        self.assertIn("OrderByDescending(x => x.Time)", dashboard)
        self.assertIn("AiRuntimeErrorJournal.Root", dashboard)
        self.assertIn("timeLocal", journal)
        self.assertIn("summary.json", journal)
        self.assertIn("error.txt", journal)
        self.assertLess(uploader.index("AiRuntimeErrorJournal.Record(report, _log, ReadRecentLog(150))"),
                        uploader.index("if (!settings.Enabled) return;"))
        self.assertIn("child.Kill(entireProcessTree: true)", (APP / "AiAutopilotHost.cs").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
