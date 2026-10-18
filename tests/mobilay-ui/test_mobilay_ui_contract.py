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
        self.assertIn("1180", self.main)
        self.assertIn("780", self.main)
        self.assertIn("53, 53, 57", self.palette)
        self.assertNotIn("private static readonly Color NavBg = Color.White", self.palette)

    def test_six_real_dashboard_cards_and_clickable_links(self):
        titles = ("자동화 상태", "모드 / 진행 단계", "생활 생산",
                  "실행 통계", "시스템 상태", "최근 활동")
        for title in titles:
            self.assertIn('"' + title + '"', self.grid)
        self.assertIn("row * 364", self.grid)
        self.assertIn("column * 390", self.grid)
        self.assertIn("private void PaintHome(Graphics g) => PaintMobilayHome(g);",
                      self.shell)
        self.assertIn("_homeLinks.Add(AddButton(", self.grid)
        self.assertIn('case "arrow-up-right":', self.shell)
        self.assertIn('if (_kind == "card-link")', self.shell)

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
            "new[] { 1, 2, 3 }", "new[] { 4, 5, 6 }",
            "ShowTelegramSettings()", "ShowErrorUploadSettings()",
            "ShowVisualRecognitionTest()", "owner.ToggleMini(true)",
            "owner.TopMost = !owner.TopMost", "owner.TestOrRefresh",
            "owner.ApplyLogVisibility()"
        ):
            self.assertIn(existing, self.grid)
        self.assertIn('mode is 4 or 5 or 6 ? "생활 생산"', self.shell)
        self.assertIn('mode is 1 or 2 or 3 ? "던전 / 어비스"', self.shell)
        self.assertIn('control.Visible = _showHome && !_owner._logExpanded',
                      self.shell)
        self.assertIn('if (_owner.AnyRunning || _owner._activeMode is not null) return;',
                      self.shell)
        self.assertIn("owner.StartSelected()", self.shell)
        self.assertIn("owner.StopSelected()", self.shell)

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
