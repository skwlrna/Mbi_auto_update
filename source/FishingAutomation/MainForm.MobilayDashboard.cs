using System.Drawing.Drawing2D;

// Presentation only: every route reuses the established MainForm handlers.
namespace FishingAutomation;

public sealed partial class MainForm
{
    private sealed partial class ReferenceDashboard
    {
        private void BuildMobilayNavigation(MainForm owner)
        {
            void OpenLog()
            {
                owner._logExpanded = true;
                owner.ApplyLogVisibility();
                UpdateAbyssDungeonPickerVisibility();
            }
            AddButton("대시보드", new(16, 135, 200, 49), () =>
            {
                _showHome = true;
                _selectedNavigation = "대시보드";
                owner._productionPageMode = null;
                owner._logExpanded = false;
                owner.ApplyLogVisibility();
                UpdateAbyssDungeonPickerVisibility();
            }, "home", "nav");
            AddButton("자동낚시", new(16, 220, 200, 49), () => SelectMode(0), "fish", "nav");
            AddButton("자동가공", new(16, 271, 200, 49), () => SelectMode(4), "leaf", "nav");
            AddButton("어비스", new(16, 322, 200, 49), () => SelectMode(2), "abyss", "nav");
            AddButton("던전", new(16, 373, 200, 49), () => SelectMode(1), "swords", "nav");
            AddButton("채집 / 제작", new(16, 424, 200, 49), () => ShowMenu(
                new[] { "자동 채집", "제작" }, i => SelectMode(new[] { 5, 6 }[i]), 228, 424), "leaf", "nav");
            AddButton("스케줄러", new(16, 475, 200, 49), () => SelectMode(7), "calendar-clock", "nav");
            AddButton("실행 기록", new(16, 560, 200, 49), OpenLog, "log", "nav");
            AddButton("알림 관리", new(16, 611, 200, 49), owner.ShowTelegramSettings, "bell", "nav");
            AddButton("환경 설정", new(16, 662, 200, 49), () => ShowSettingsMenu(228, 662), "settings", "nav");
            AddButton("미니 모드", new(16, 822, 200, 40), () => owner.ToggleMini(true), "", "quiet");
            var alwaysTop = AddButton("전체 최상단", new(16, 869, 200, 40),
                () => owner.TopMost = !owner.TopMost, "", "quiet");
            alwaysTop.Click += (_, _) =>
                alwaysTop.Text = owner.TopMost ? "최상단 해제" : "전체 최상단";
            AddButton("전체 중지", new(16, 1001, 200, 55), owner.StopSelected, "", "danger");
            AddButton("F8 테스트", new(1032, 8, 169, 48), owner.TestOrRefresh, "refresh", "quiet");

            Action[] targets = { () => SelectMode(0), () => SelectMode(4), () => SelectMode(2),
                () => SelectMode(7), () => SelectMode(5), () => SelectMode(6) };
            for (int i = 0; i < targets.Length; i++)
            {
                int col = i % 3, row = i / 3;
                int x = 256 + col * 390, y = 469 + row * 174;
                _homeLinks.Add(AddButton("카드 열기 " + (i + 1),
                    new(x + 328, y + 12, 33, 33), targets[i], "arrow-up-right", "card-link"));
            }
        }

        private void ShowSettingsMenu(int x, int y) => ShowMenu(
            new[] { "텔레그램 알림", "오류 전송", "인식 테스트" }, i =>
            {
                if (i == 0) _owner.ShowTelegramSettings();
                else if (i == 1) _owner.ShowErrorUploadSettings();
                else _owner.ShowVisualRecognitionTest();
            }, x, y);

        private void PaintBentoSidebar(Graphics g)
        {
            TextAt(g, "메인", new(22, 104, 180, 29), 15, true, Muted);
            TextAt(g, "자동화", new(22, 186, 180, 29), 15, true, Muted);
            TextAt(g, "관리", new(22, 528, 180, 29), 15, true, Muted);
            using var pen = new Pen(Line);
            g.DrawLine(pen, 16, 929, 216, 929);
            Icon(g, "game", new(21, 945, 25, 25), Blue);
            TextAt(g, "게임 연결", new(53, 941, 160, 28), 15, true);
            TextAt(g, _owner._sysGameValue.Text, new(53, 967, 160, 25), 15, false, Muted);
        }

        private void PaintMobilayHome(Graphics g)
        {
            TextAt(g, "대시보드", new(257, 108, 620, 48), 30, true);
            TextAt(g, "현재 실행 상태", new(260, 157, 620, 29), 16, false, Muted);
            using (var hero = new SolidBrush(Color.FromArgb(36, 40, 59)))
            using (var path = Round(new RectangleF(256, 198, 1166, 251), 20))
                g.FillPath(hero, path);
            var white = Color.White;
            var soft = Color.FromArgb(203, 210, 234);
            Dot(g, 285, 228, 6, _owner.AnyRunning ? Color.FromArgb(134, 239, 172) : soft);
            TextAt(g, _owner._statusValue.Text, new(301, 211, 1000, 32), 18, true, soft);
            TextAt(g, _owner._currentModeValue.Text, new(280, 246, 800, 57), 32, true, white);
            TextAt(g, _owner._currentDungeonValue.Text, new(280, 301, 570, 34), 18, false, soft);
            TextAt(g, _owner._currentStepValue.Text, new(280, 337, 770, 35), 16, false, soft);
            TextAt(g, "진행률 " + _owner._progress.Value + "%", new(1100, 344, 290, 35), 19, true, white, StringAlignment.Far);
            TextAt(g, "경과 " + _owner._elapsedValue.Text, new(1060, 258, 330, 42), 19, true, white, StringAlignment.Far);
            using (var track = new SolidBrush(Color.FromArgb(70, 75, 99)))
            using (var path = Round(new RectangleF(280, 397, 1110, 13), 6))
                g.FillPath(track, path);
            if (_owner._progress.Value > 0)
            {
                using var fill = new SolidBrush(Color.FromArgb(165, 180, 252));
                using var path = Round(new RectangleF(280, 397, Math.Max(12, 1110 * _owner._progress.Value / 100f), 13), 6);
                g.FillPath(fill, path);
            }

            void Tile(int col, int row, string title, string detail, string icon, Color bg)
            {
                int x = 256 + col * 390, y = 469 + row * 174;
                using (var fill = new SolidBrush(bg))
                using (var path = Round(new RectangleF(x, y, 374, 154), 18))
                    g.FillPath(fill, path);
                Icon(g, icon, new(x + 20, y + 18, 30, 30), Blue);
                TextAt(g, title, new(x + 20, y + 58, 334, 39), 23, true);
                TextAt(g, detail, new(x + 20, y + 101, 334, 30), 16, false, Muted);
            }
            Tile(0, 0, "자동낚시", "낚시 설정 및 실행", "fish", Color.FromArgb(220, 233, 247));
            Tile(1, 0, "자동가공", "품목 및 수량 설정", "leaf", Color.FromArgb(224, 231, 255));
            Tile(2, 0, "어비스", "던전 선택 및 실행", "abyss", Color.FromArgb(235, 226, 250));
            Tile(0, 1, "스케줄러", "작업 순서 및 실행 설정", "calendar-clock", Color.FromArgb(220, 238, 227));
            Tile(1, 1, "자동채집", "채집 설정 및 실행", "leaf", Color.FromArgb(228, 242, 233));
            Tile(2, 1, "제작", "제작 설정 및 실행", "list", Color.FromArgb(245, 232, 213));

            Card(g, new(256, 817, 1166, 130));
            TextAt(g, "실행 기록", new(278, 827, 240, 34), 19, true);
            TextAt(g, "완료 / 시도  " + _owner._successValue.Text + " / " + _owner._roundValue.Text,
                new(525, 827, 335, 34), 16, false, Muted);
            TextAt(g, "F8 테스트  ·  F9 시작  ·  F10 정지", new(962, 827, 433, 34), 14, false, Muted, StringAlignment.Far);
            var activity = string.IsNullOrWhiteSpace(_owner._progressLogPreview.Text)
                ? "최근 활동 기록이 없습니다." : _owner._progressLogPreview.Text;
            TextAt(g, activity, new(278, 870, 1115, 65), 16, false, TitleText, multiline: true);
        }
    }
}
