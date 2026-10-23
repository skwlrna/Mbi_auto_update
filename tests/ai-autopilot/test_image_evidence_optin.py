"""Consent-gated local screenshot evidence for the real Codex watcher.

All Codex, UI and network operations are mocked; no game or secrets needed.
"""
import importlib.util
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
AI = ROOT / "source" / "FishingAutomation" / "ai-autopilot"
sys.path.insert(0, str(AI))
spec = importlib.util.spec_from_file_location("image_evidence_agent", AI / "autopilot.py")
agent = importlib.util.module_from_spec(spec)
spec.loader.exec_module(agent)


class ScreenshotConsentTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.home = Path(self.tmp.name) / "MabiAutoAI"
        self.results = self.home / "autopilot-results"
        self.results.mkdir(parents=True)
        self.runtime = self.home / "runtime-reports"
        self.runtime.mkdir()
        self.event = self.runtime / "runtime_20261024_043000_abcd1234"
        self.event.mkdir()
        self.png = self.event / "screen.png"
        # Sufficient PNG IHDR header for a bounded, non-decoding file guard.
        self.png.write_bytes(
            b"\x89PNG\r\n\x1a\n" + (13).to_bytes(4, "big") + b"IHDR"
            + (800).to_bytes(4, "big") + (1000).to_bytes(4, "big") + b"x" * 20
        )
        self.settings = agent.config()
        self.settings["runtime_reports_dir"] = str(self.runtime)
        self.settings["codex_max_retries"] = 0
        self.consent = self.results / "image-opt-in.json"
        self.patcher = mock.patch.object(agent, "DEFAULT_HOME", self.home)
        self.patcher.start()
        self.addCleanup(self.patcher.stop)

    def permit(self, enabled=True):
        self.consent.write_text(json.dumps({
            "schema": 1, "enabled": enabled,
            "scope": "local_runtime_screenshot_to_codex"}), encoding="utf-8")

    def test_default_off_and_mcp_screenshots_never_attached(self):
        self.assertIsNone(agent.consented_error_image(self.event, self.settings))
        self.permit()
        self.assertEqual(agent.consented_error_image(self.event, self.settings), self.png.resolve())
        other = self.home / "MabiAutoMCP" / "reports" / "error_1"
        other.mkdir(parents=True)
        (other / "screen.png").write_bytes(self.png.read_bytes())
        self.assertIsNone(agent.consented_error_image(other, self.settings))
        self.permit(False)
        self.assertIsNone(agent.consented_error_image(self.event, self.settings))

    def test_invalid_image_and_invalid_consent_are_rejected(self):
        self.permit()
        self.png.write_bytes(b"not a png")
        self.assertIsNone(agent.consented_error_image(self.event, self.settings))
        self.png.write_bytes(
            b"\x89PNG\r\n\x1a\n" + (13).to_bytes(4, "big") + b"IHDR"
            + (10000).to_bytes(4, "big") + (1000).to_bytes(4, "big"))
        self.assertIsNone(agent.consented_error_image(self.event, self.settings))
        self.consent.write_text('{"enabled":true}', encoding="utf-8")
        self.assertIsNone(agent.consented_error_image(self.event, self.settings))

    def test_codex_receives_one_image_only_while_consent_is_valid(self):
        self.permit()
        image = agent.consented_error_image(self.event, self.settings)
        self.assertIsNotNone(image)
        seen = []
        output = self.home / "codex-output"
        output.mkdir()
        def fake_run(argv, _cwd, _prompt, _events, _stderr, **_kwargs):
            seen.append(argv)
            message_file = Path(argv[argv.index("--output-last-message") + 1])
            message_file.write_text("completed", encoding="utf-8")
            return 0, '{"type":"turn.completed"}\n', "", None
        with mock.patch.object(agent.shutil, "which", return_value="codex.exe"), \
             mock.patch.object(agent, "require_isolated_ai_mode"), \
             mock.patch.object(agent, "supervised_run", side_effect=fake_run):
            ok, _ = agent.exec_codex(self.home, "review evidence", output,
                                     self.settings, image=image)
            self.assertTrue(ok)
            self.assertEqual(seen[-1].count("--image"), 1)
            self.assertEqual(seen[-1][seen[-1].index("--image") + 1], str(image))
            self.permit(False)
            ok, _ = agent.exec_codex(self.home, "review evidence", output,
                                     self.settings, image=image)
            self.assertTrue(ok)
            self.assertNotIn("--image", seen[-1])
            ok, _ = agent.exec_codex(self.home, "no evidence", output,
                                     self.settings)
            self.assertTrue(ok)
            self.assertNotIn("--image", seen[-1])

    def test_runtime_records_image_before_optional_github_upload(self):
        uploader = (ROOT / "source/FishingAutomation/RuntimeErrorUploader.cs").read_text(encoding="utf-8")
        journal = (ROOT / "source/FishingAutomation/AiRuntimeErrorJournal.cs").read_text(encoding="utf-8")
        ui = (ROOT / "source/FishingAutomation/AiAutopilotDashboard.cs").read_text(encoding="utf-8")
        self.assertIn("AiAutopilotDashboard.ImageEvidenceEnabled", uploader)
        self.assertLess(uploader.index("AiRuntimeErrorJournal.Record(report"),
                        uploader.index("if (!settings.Enabled) return;"))
        self.assertIn('Path.Combine(directory, "screen.png")', journal)
        self.assertIn('Text = "오류 사진을 Codex에 전달 (동의 필요)"', ui)
        self.assertIn("local_runtime_screenshot_to_codex", ui)


if __name__ == "__main__":
    unittest.main()
