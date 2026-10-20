"""Stage 16 safety checks on the opt-in Windows diagnostic entrypoint.

These tests are static guard tests; CI also compiles and smoke-tests the
actual preview executable. No game, native capture, macro UI or AI runs.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
APP = ROOT / "source" / "FishingAutomation"
PROBE = ROOT / "tools" / "Stage16Preview"

class Stage16PreviewContractTests(unittest.TestCase):
    def test_preview_is_the_only_opted_in_winforms_entrypoint(self):
        program = (APP / "Program.cs").read_text(encoding="utf-8-sig")
        self.assertIn("#if STAGE16_PREVIEW_ONLY", program)
        self.assertIn("Application.Run(new Stage16ReadOnlyPreviewForm());", program)
        self.assertIn("#else\n        Application.Run(new MainForm());\n#endif", program)
        self.assertIn("--stage16-self-test", program)
        project = (APP / "FishingAutomation.csproj").read_text(encoding="utf-8")
        self.assertIn("Condition=\"'$(Stage16PreviewOnly)' == 'true'\"", project)
        self.assertIn("STAGE16_PREVIEW_ONLY", project)
        self.assertNotIn("<Stage16PreviewOnly>true</Stage16PreviewOnly>", project)

    def test_standalone_preview_never_starts_production_services(self):
        view = (APP / "Stage16ReadOnlyPreviewForm.cs").read_text(
            encoding="utf-8")
        self.assertIn("#if STAGE16_PREVIEW_ONLY", view)
        self.assertIn("private async Task CheckOnceAsync()", view)
        self.assertIn("_run.Click +=", view)
        self.assertIn("explicitClick: true", view)
        self.assertIn("AnotherMacroProcessRunning()", view)
        self.assertIn("Stage16PreviewGate.ExactReadOnlyQueries", view)
        for forbidden in (
            "new MainForm(", "new FishingBot(", "StartRemoteControl(",
            "CheckForUpdatesAsync(", "StartSelected(", "AutoStartAiAutopilot(",
            "AiRuntimeErrorJournal.Record(", "UploadAlertAsync(",
            "SendRuntimeAlertAsync(", "PublishUpdate(", "Application.Restart(",
            "Process.Start(", "SaveSettings(",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, view)
        self.assertIn("AiPassiveCliEvidence.SetEnabled(false);", view)
        self.assertIn("no real AI worker", view)

    def test_cli_query_is_user_click_only_and_delivery_is_whitelisted(self):
        source = (APP / "Stage16ReadOnlyPreviewForm.cs").read_text(
            encoding="utf-8")
        constructor = source[:source.index("    private async Task CheckOnceAsync()")]
        self.assertNotIn("ReadOnceAsync(", constructor)
        self.assertNotIn("new MabinogiMobileCli(", constructor)
        self.assertIn("Click += async", constructor)
        self.assertIn("private async Task CheckOnceAsync()", source)
        workflow = (ROOT / ".github" / "workflows" /
                    "packet-observation-checks.yml").read_text(encoding="utf-8")
        self.assertIn("-p:Stage16PreviewOnly=true", workflow)
        self.assertIn("--stage16-self-test", workflow)
        self.assertIn("MabiAuto-Stage16-Isolated-ReadOnly-Win64", workflow)
        self.assertIn("Copy-Item $exe", workflow)
        self.assertIn("Compare-Object", workflow)
        command = (PROBE / "Run_Stage16_ReadOnly.cmd").read_text(
            encoding="utf-8")
        self.assertIn("choice /C YN", command)
        self.assertIn("FishingAutomation.exe", command)
        self.assertNotIn("START.cmd", command)
        usage = (PROBE / "사용안내.txt").read_text(encoding="utf-8")
        self.assertIn("운영 설치 폴더", usage)
        self.assertIn("CLI 질의 없음", usage)

if __name__ == "__main__":
    unittest.main()
