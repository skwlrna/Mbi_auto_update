namespace FishingAutomation;

public sealed partial class MainForm
{
    private sealed partial class ReferenceDashboard
    {
        private void BuildWorkspaceNavigation(MainForm owner)
        {
            void OpenOverview(bool settings, bool log)
            {
                _showHome = !settings && !log;
                _showSettings = settings;
                _selectedNavigation = settings ? "설정" : log ? "기록" : "홈";
                owner._productionPageMode = null;
                owner._logExpanded = log;
                owner.ApplyLogVisibility();
                UpdateAbyssDungeonPickerVisibility();
            }

            (string Text, Action Open)[] tabs =
            [
                ("홈", () => OpenOverview(false, false)), ("가공", () => SelectMode(4)),
                ("채집", () => SelectMode(5)), ("제작", () => SelectMode(6)), ("낚시", () => SelectMode(0)),
                ("전투", () => SelectMode(owner._mode.SelectedIndex == 1 ? 1 : 2)),
                ("스케줄러", () => SelectMode(7)), ("기록", () => OpenOverview(false, true)),
                ("설정", () => OpenOverview(true, false))
            ];
            for (int i = 0; i < tabs.Length; i++)
                AddButton(tabs[i].Text, new(233 + 107 * i, 2, 105, 62), tabs[i].Open, "", "nav");

            void Setting(string label, Action run, int x, int y) =>
                _settingsControls.Add(AddButton(label, new(x, y, 310, 56), run, "", "quiet"));

            Setting("브라우저 원격 제어", owner.ShowBrowserControlSettings, 270, 180);
            Setting("텔레그램 알림", owner.ShowTelegramSettings, 610, 180);
            Setting("오류 전송", owner.ShowErrorUploadSettings, 950, 180);
            Setting("테마 전환", ToggleTheme, 270, 285);
            Setting("미니 모드", () => owner.ToggleMini(true), 610, 285);
            Setting("항상 위 전환", () => owner.TopMost = !owner.TopMost, 950, 285);
            Setting("인식 테스트", owner.ShowVisualRecognitionTest, 270, 385);
            Setting("F8 테스트 / 새로고침", owner.TestOrRefresh, 610, 385);
        }

        private void PaintWorkspaceSettings(Graphics g)
        {
            TextAt(g, "설정", new(256, 82, 520, 65), 34, true);
            TextAt(g, "연동", new(270, 150, 300, 27), 17, true);
            TextAt(g, "일반", new(270, 252, 300, 27), 17, true);
            TextAt(g, "진단", new(270, 353, 300, 27), 17, true);
            TextAt(g, UpdateManager.CurrentVersion, new(610, 487, 300, 49), 17, false, Muted);
        }

        private void PaintWorkspaceHome(Graphics g)
        {
            TextAt(g, "홈", new(256, 82, 560, 78), 34, true);
            Card(g, new(256, 182, 690, 365));
            TextAt(g, "현재 작업", new(280, 205, 620, 42), 22, true);
            TextAt(g, _owner.AnyRunning ? _owner._currentModeValue.Text : "대기 중",
                new(280, 270, 620, 58), 33, true);
            TextAt(g, _owner._statusValue.Text, new(280, 345, 620, 75), 20, true, multiline: true);
            TextAt(g, _owner._elapsedValue.Text, new(280, 435, 620, 42), 17, false, Muted);
            using (var track = new SolidBrush(CardBg2)) g.FillRectangle(track, 280, 500, 630, 12);
            using (var fill = new SolidBrush(Blue)) g.FillRectangle(fill, 280, 500,
                Math.Clamp(_owner._progress.Value, 0, 100) * 6.3f, 12);

            Card(g, new(965, 182, 457, 365));
            TextAt(g, "작업 상세", new(991, 205, 400, 42), 22, true);
            TextAt(g, _owner._currentDungeonValue.Text, new(991, 267, 397, 44), 20, true);
            TextAt(g, _owner._currentStepValue.Text, new(991, 326, 397, 104), 18, false, multiline: true);
            TextAt(g, _owner._recentDetectValue.Text, new(991, 443, 397, 60), 16, false, Muted);

            Card(g, new(256, 574, 1166, 350));
            TextAt(g, "최근 기록", new(280, 593, 1060, 45), 22, true);
            var lines = _owner._progressLogPreview.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).TakeLast(5).ToArray();
            TextAt(g, lines.Length == 0 ? "최근 기록이 없습니다." : string.Join("\n", lines), new(280, 654, 1106, 228), 17, false, Muted, multiline: true);
        }
    }
}
