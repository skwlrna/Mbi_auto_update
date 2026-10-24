"""Static safety contract: no legacy Codex watcher while GitHub error reporting persists."""
from pathlib import Path
import unittest

BASE = Path(__file__).resolve().parents[2]
APP = BASE / "source" / "FishingAutomation"

class LegacyAutofixRetirement(unittest.TestCase):
    def test_no_agent_sources(self):
        for name in ("AiAutopilotDashboard.cs", "AiAutopilotDashboard.Cleanup.cs",
                     "MainForm.AiAutopilot.cs", "AiAutopilotHost.cs",
                     "AiOneClickTestHost.cs", "AiRuntimeErrorJournal.cs"):
            self.assertFalse((APP / name).exists(), name)
        self.assertFalse((APP / "ai-autopilot").exists())

    def test_no_autostart_or_bundle(self):
        main = (APP / "MainForm.cs").read_text(encoding="utf-8")
        csproj = (APP / "FishingAutomation.csproj").read_text(encoding="utf-8")
        for token in ("InitializeAiAutopilot","AutoStartAiAutopilot",
                      "BuildAiAutopilotPanel","DisposeAiAutopilot"):
            self.assertNotIn(token, main)
        self.assertNotIn("ai-autopilot", csproj)

    def test_runtime_logging_works_independently(self):
        main = (APP / "MainForm.cs").read_text(encoding="utf-8")
        upload = (APP / "RuntimeErrorUploader.cs").read_text(encoding="utf-8")
        self.assertIn("new RuntimeErrorUploader(_log, runtimeLogPath)", main)
        self.assertIn("await PutFileAsync(repo, branch, token,", upload)
        self.assertIn("settings.SendScreenshot", upload)
        self.assertIn("CaptureGameWindowPng()", upload)
        self.assertNotIn("AiAutopilotDashboard", upload)
        self.assertNotIn("AiRuntimeErrorJournal", upload)

if __name__ == "__main__":
    unittest.main()
