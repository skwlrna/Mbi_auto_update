"""Offline UI-only regression contracts for the user's Mobilay screenshot.

Never launches WinForms, connects to the game, captures packets, installs assets,
executes a macro or fabricates dashboard values.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[2]
UI = ROOT / "source" / "FishingAutomation"


class MobilayPresentationContract(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.main = (UI / "MainForm.cs").read_text(encoding="utf-8")
        cls.palette = (UI / "MainForm.Dashboard.cs").read_text(encoding="utf-8")
        cls.shell = (UI / "MainForm.ReferenceUI.cs").read_text(encoding="utf-8")
        cls.grid = (UI / "MainForm.MobilayDashboard.cs").read_text(encoding="utf-8")

    def test_charcoal_purple_theme_and_proportions(self):
        for rgb in ("23, 23, 24", "13, 14, 18", "171, 143, 249",
                    "238, 238, 244", "81, 200, 156"):
            self.assertIn(rgb, self.main)
        self.assertIn("1120", self.main)
        self.assertIn("720", self.main)
        self.assertIn("53, 53, 57", self.palette)
        self.assertNotIn("private static readonly Color NavBg = Color.White", self.palette)

    def test_workspace_and_engine_palettes_are_separate(self):
        self.assertIn("Color NavBg => _darkTheme", self.shell)
        self.assertIn(": Color.White;", self.shell)
        self.assertIn("Color.FromArgb(7, 81, 213)", self.shell)

    def test_header_and_body_resize_independently(self):
        for marker in ("MapBounds(r)", "UnmapX(e.X)", "MapX(x)", "ContentScaleX", "ToggleTheme()"):
            self.assertIn(marker, self.shell)
        self.assertIn("bool header = r.Y < 65f", self.shell)
        self.assertIn('Setting("테마 전환", ToggleTheme', self.grid)

    def test_task_pages_share_presentation_palette(self):
        palette = (UI / "MainForm.TaskPalette.cs").read_text(encoding="utf-8")
        self.assertIn("private static void ApplyTaskPalette", palette)
        self.assertIn("TaskPalette For(Control? control)", palette)
        for page in ("ProductionPage", "CraftingPage", "SchedulerPage"):
            source = (UI / ("MainForm." + page + ".cs")).read_text(encoding="utf-8")
            self.assertIn("ITaskThemePage", source)
            self.assertIn("ApplyTaskPalette(this, previous, next)", source)
        for field in ("_alteringPage", "_gatheringPage", "_craftingPage", "_schedulerPage"):
            self.assertIn(field + ".SetTaskTheme(_darkTheme)", self.shell)

    def test_browser_control_is_explicit_loopback_and_authenticated(self):
        server = (UI / "BrowserControlServer.cs").read_text(encoding="utf-8")
        bridge = (UI / "MainForm.BrowserControl.cs").read_text(encoding="utf-8")
        assets = (UI / "BrowserControlAssets.cs").read_text(encoding="utf-8")
        self.assertIn("http://127.0.0.1:18765/", server)
        self.assertIn("IPAddress.IsLoopback", server)
        self.assertIn("FixedTimeEquals", server)
        self.assertIn("request.HttpMethod != \"POST\"", server)
        self.assertIn("RandomNumberGenerator.GetBytes(32)", server)
        self.assertIn("HandlePhoneAlteringCommand(command, key)", bridge)
        self.assertIn("BeginInvoke", bridge)
        self.assertIn("StartSelected()", bridge)
        self.assertIn("StopSelected()", bridge)
        self.assertIn("StopBrowserControl();", self.main)
        self.assertNotIn("localStorage", assets)
        self.assertNotIn("http://+", server)
        self.assertNotIn("http://*", server)

    def test_home_has_live_tasks_and_no_duplicate_navigation(self):
        for heading in ("현재 작업", "작업 상세", "최근 기록"):
            self.assertIn(f'TextAt(g, "{heading}"', self.grid)
        self.assertIn("PaintWorkspaceHome(g)", self.shell)
        self.assertNotIn("카드 열기", self.grid)
        self.assertIn('if (_kind == "nav")', self.shell)

    def test_updates_remain_available_in_settings(self):
        self.assertIn('owner.CheckForUpdatesAsync(true)', self.shell)
        self.assertIn('_settingsControls.Add(owner._updateButton)', self.shell)
        self.assertIn('owner.TopMost = !owner.TopMost', self.grid)
        self.assertNotIn('SendKeys.', self.grid)
        self.assertNotIn('SendInput(', self.grid)

    def test_no_mock_stats_or_hidden_automation_entry_points(self):
        for existing in (
            "_owner._statusValue.Text", "_owner._elapsedValue.Text",
            "_owner._currentModeValue.Text", "_owner._progress.Value",
            "_owner._currentStepValue.Text", "_owner._recentDetectValue.Text",
            "_owner._progressLogPreview.Text",
        ):
            self.assertIn(existing, self.grid)
        for prohibited in (
            "StartProcess(", "Process.Start(", "SendKeys.",
            "SendInput(", "GetPacket(", "TcpClient(", "exec_codex",
            "fakeDps", "randomDps", "Mobilay.exe", "DllImport(",
        ):
            self.assertNotIn(prohibited, self.grid)

    def test_nine_tabs_reuse_existing_macro_handlers(self):
        for title in ("홈", "가공", "채집", "제작", "낚시", "전투", "스케줄러", "기록", "설정"):
            self.assertIn(f'("{title}",', self.grid)
        for handler in ("SelectMode(0)", "SelectMode(4)", "SelectMode(5)", "SelectMode(6)",
                        "SelectMode(7)", "owner.ShowTelegramSettings", "owner.ShowErrorUploadSettings",
                        "owner.ShowVisualRecognitionTest", "owner.ToggleMini(true)", "owner.ApplyLogVisibility()"):
            self.assertIn(handler, self.grid)
        self.assertIn('if (_owner.AnyRunning || _owner._activeMode is not null) return;', self.shell)
        self.assertIn("owner.StartSelected()", self.shell)
        self.assertIn("owner.StopSelected()", self.shell)

    def test_combat_offers_only_abyss_and_regular_dungeons(self):
        for title in ("던전", "어비스"):
            self.assertIn(f'_modeTabs.Add(AddButton("{title}",', self.shell)
        self.assertNotIn('_modeTabs.Add(AddButton("페카 심층",', self.shell)
        self.assertNotIn("페카 심층", self.grid)
        self.assertIn('new[] { "허상의 정박지", "광기의 동굴", "흩어진 물길" }', self.shell)

    def test_error_logging_remains_available_after_ai_removal(self):
        self.assertIn("new RuntimeErrorUploader(_log, runtimeLogPath)", self.main)
        self.assertNotIn("AutoStartAiAutopilot", self.main)
        self.assertNotIn("BuildAiAutopilotPanel", self.main)
        self.assertIn("owner.ShowErrorUploadSettings", self.grid)

    def test_dashboard_reuses_gdi_fonts_and_skips_unchanged_layout(self):
        self.assertIn("_textFonts.TryGetValue", self.shell)
        self.assertIn("foreach (var font in _textFonts.Values) font.Dispose()", self.shell)
        self.assertIn("if (control.Bounds != bounds) control.Bounds = bounds;", self.shell)

    def test_does_not_embed_third_party_assets(self):
        for path in (
            UI / "MainForm.MobilayDashboard.cs",
            UI / "MainForm.ReferenceUI.cs",
        ):
            value = path.read_text(encoding="utf-8")
            self.assertNotIn("Resources\\Image\\", value)
            self.assertNotIn('GetManifestResourceStream("Mobilay', value)
            self.assertNotIn("www.mobilay", value)
        # Styling does not alter core automation classes, and all read-only
        # data accesses use the existing MainForm runtime state.
        self.assertIn("PaintWorkspaceHome", self.grid)


if __name__ == "__main__":
    unittest.main()
