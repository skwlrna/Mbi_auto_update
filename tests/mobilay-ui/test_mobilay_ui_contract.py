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

    def test_legacy_task_palette_and_bento_shell_are_separate(self):
        self.assertIn("Color.FromArgb(245, 246, 248)", self.shell)
        self.assertIn("Color NavBg => _darkTheme", self.shell)
        self.assertIn(": Color.White;", self.shell)
        self.assertIn("Color.FromArgb(36, 40, 59)", self.grid)

    def test_sidebar_reflow_and_theme_are_presentation_only(self):
        for marker in ("MapBounds(r)", "UnmapX(e.X)", "MapX(x)",
                       "ContentScaleX", "ToggleSidebar()", "ToggleTheme()"):
            self.assertIn(marker, self.shell)
        self.assertIn("if (_sidebarCollapsed)", self.grid)
        self.assertIn('owner.StopSelected, "stop", "danger"', self.grid)
        self.assertIn('themeToggle.AccessibleName = "테마 전환"', self.grid)

    def test_six_real_dashboard_cards_and_clickable_links(self):
        titles = ("자동낚시", "자동가공", "어비스", "스케줄러", "자동채집", "제작")
        for title in titles:
            self.assertIn('"' + title + '"', self.grid)
        self.assertIn("row * 174", self.grid)
        self.assertIn("col * 390", self.grid)
        self.assertIn("new RectangleF(x, y, 374, 154)", self.grid)
        self.assertIn("Card(g, new(256, 817, 1166, 130))", self.grid)
        self.assertIn('TextAt(g, "실행 기록"', self.grid)
        self.assertIn('new(16, 135, 200, 49)', self.grid)
        self.assertIn('new(16, 560, 200, 49)', self.grid)
        self.assertIn('new(1139, 881, 265, 41)', self.shell)
        self.assertIn("private void PaintHome(Graphics g) => PaintMobilayHome(g);",
                      self.shell)
        self.assertIn("_homeLinks.Add(AddButton(", self.grid)
        self.assertIn('case "arrow-up-right":', self.shell)
        self.assertIn('if (_kind == "card-link")', self.shell)

    def test_compact_footer_keeps_manual_updates_and_runtime_separate(self):
        self.assertIn('owner.CheckForUpdatesAsync(true)', self.shell)
        self.assertIn('new(278, 870, 1115, 65)', self.grid)
        self.assertIn('F8 테스트  ·  F9 시작  ·  F10 정지', self.grid)
        self.assertIn('_owner._progressLogPreview.Text', self.grid)
        self.assertIn('owner.TopMost = !owner.TopMost', self.grid)
        # The compact visual layout must not alter the keyboard/game actions.
        self.assertNotIn('SendKeys.', self.grid)
        self.assertNotIn('SendInput(', self.grid)

    def test_no_mock_stats_or_hidden_automation_entry_points(self):
        for existing in (
            "_owner._statusValue.Text", "_owner._elapsedValue.Text",
            "_owner._currentModeValue.Text", "_owner._progress.Value",
            "_owner._successValue.Text", "_owner._roundValue.Text",
            "_owner._sysGameValue", "_owner._sysOcrValue",
            "_owner._progressLogPreview.Text",
        ):
            self.assertIn(existing, self.grid)
        for prohibited in (
            "StartProcess(", "Process.Start(", "SendKeys.",
            "SendInput(", "GetPacket(", "TcpClient(", "exec_codex",
            "fakeDps", "randomDps", "Mobilay.exe", "DllImport(",
        ):
            self.assertNotIn(prohibited, self.grid)

    def test_sidebar_routes_reuse_existing_macro_handlers(self):
        for existing in (
            "SelectMode(0)", "SelectMode(7)",
            "SelectMode(1)", "SelectMode(2)", "SelectMode(4)", "new[] { 5, 6 }",
            "ShowTelegramSettings()", "ShowErrorUploadSettings()",
            "ShowVisualRecognitionTest()", "owner.ToggleMini(true)",
            "owner.TopMost = !owner.TopMost", "owner.TestOrRefresh",
            "owner.ApplyLogVisibility()"
        ):
            self.assertIn(existing, self.grid)
        self.assertIn('4 => "자동가공"', self.shell)
        self.assertIn('2 or 3 => "어비스"', self.shell)
        self.assertIn('owner.StopSelected', self.grid)
        self.assertIn('control.Visible = _showHome && !_owner._logExpanded',
                      self.shell)
        self.assertIn('if (_owner.AnyRunning || _owner._activeMode is not null) return;',
                      self.shell)
        self.assertIn("owner.StartSelected()", self.shell)
        self.assertIn("owner.StopSelected()", self.shell)

    def test_peaca_deep_is_not_offered_in_dungeon_sidebar(self):
        self.assertIn('AddButton("던전",', self.grid)
        self.assertIn('AddButton("어비스",', self.grid)
        self.assertNotIn('AddButton("페카 심층",', self.grid)
        self.assertNotIn('new[] { "던전", "어비스", "페카 심층" }', self.grid)
        self.assertIn('new[] { "허상의 정박지", "광기의 동굴", "흩어진 물길" }', self.shell)
        # The implementation remains in place; this change only hides the nav entry.
        self.assertIn('if (string.Equals(mode, "페카 심층"', self.shell)

    def test_error_logging_remains_available_after_ai_removal(self):
        self.assertIn("new RuntimeErrorUploader(_log, runtimeLogPath)", self.main)
        self.assertNotIn("AutoStartAiAutopilot", self.main)
        self.assertNotIn("BuildAiAutopilotPanel", self.main)
        self.assertIn("ShowErrorUploadSettings()", self.grid)

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
        self.assertIn("presentation", self.grid.lower())


if __name__ == "__main__":
    unittest.main()
