using System.Drawing.Drawing2D;

// Presentation only. MabiAuto's established F8/F9/F10, dungeon, processing,
// scheduler and Codex security handlers remain the sole execution paths.
// Geometry and charcoal/lavender styling follow the owner's Mobilay screenshot;
// no third-party copyrighted assets or logos are embedded.
namespace FishingAutomation;

public sealed partial class MainForm
{
    private sealed partial class ReferenceDashboard
    {
        private void BuildMobilayNavigation(MainForm owner)
        {
            AddButton("홈", new(16, 135, 200, 54), () =>
            {
                _showHome = true;
                _selectedNavigation = "홈";
                owner._productionPageMode = null;
                owner._logExpanded = false;
                owner.ApplyLogVisibility();
                UpdateAbyssDungeonPickerVisibility();
            }, "home", "nav");
            AddButton("자동화 실행", new(16, 204, 200, 54), () => SelectMode(0), "play", "nav");
            AddButton("던전 / 어비스", new(16, 273, 200, 54), () => ShowMenu(
                new[] { "던전", "어비스", "페카 심층" },
                i => SelectMode(new[] { 1, 2, 3 }[i]), 228, 273), "swords", "nav");
            AddButton("생활 생산", new(16, 342, 200, 54), () => ShowMenu(
                new[] { "자동 가공", "자동 채집", "제작" },
                i => SelectMode(new[] { 4, 5, 6 }[i]), 228, 342), "leaf", "nav");
            AddButton("스케줄러", new(16, 411, 200, 54), () => SelectMode(7), "calendar-clock", "nav");
            AddButton("기타 / 설정", new(16, 480, 200, 54), () => ShowMenu(
                new[] { "텔레그램 알림", "오류 전송", "인식 테스트" }, i =>
                {
                    if (i == 0) owner.ShowTelegramSettings();
                    else if (i == 1) owner.ShowErrorUploadSettings();
                    else owner.ShowVisualRecognitionTest();
                }, 228, 480), "settings", "nav");
            AddButton("로그 / 진단", new(16, 549, 200, 54), () =>
            {
                owner._logExpanded = true;
                owner.ApplyLogVisibility();
                UpdateAbyssDungeonPickerVisibility();
            }, "log", "nav");

            // Mobilay-style bottom sidebar controls. No gameplay, network,
            // packet or automated repair is performed by presentation actions.
            AddButton("탭 접기", new(16, 809, 200, 45), () => owner.ToggleMini(true), "", "quiet");
            AddButton("창 최소화", new(16, 862, 200, 45),
                () => owner.WindowState = FormWindowState.Minimized, "", "quiet");
            var alwaysTop = AddButton("전체 최상단", new(16, 915, 200, 45),
                () => owner.TopMost = !owner.TopMost, "", "quiet");
            alwaysTop.Click += (_, _) =>
                alwaysTop.Text = owner.TopMost ? "최상단 해제" : "전체 최상단";
            AddButton("설정", new(16, 968, 200, 45), () => ShowSettingsMenu(228, 968),
                "settings", "quiet");

            AddButton("F8 테스트", new(856, 8, 170, 48),
                owner.TestOrRefresh, "refresh", "quiet");
            AddButton("설정", new(1032, 8, 169, 48),
                () => ShowSettingsMenu(1050, 60), "settings", "quiet");

            Action[] targets =
            {
                () => SelectMode(0),
                () => SelectMode(7),
                () => SelectMode(4),
                () => SelectMode(2),
                () => owner.ShowVisualRecognitionTest(),
                () => { owner._logExpanded = true;
                        owner.ApplyLogVisibility();
                        UpdateAbyssDungeonPickerVisibility(); }
            };
            for (int i = 0; i < targets.Length; i++)
            {
                int col = i % 3, row = i / 3;
                int x = 256 + col * 390, y = 210 + row * 364;
                _homeLinks.Add(AddButton("카드 열기 " + (i + 1),
                    new(x + 315, y + 16, 37, 37), targets[i],
                    "arrow-up-right", "card-link"));
            }
        }

        private void ShowSettingsMenu(int x, int y) => ShowMenu(
            new[] { "텔레그램 알림", "오류 전송", "인식 테스트" }, i =>
            {
                if (i == 0) _owner.ShowTelegramSettings();
                else if (i == 1) _owner.ShowErrorUploadSettings();
                else _owner.ShowVisualRecognitionTest();
            }, x, y);

        // Mobilay six-card grid with *only actual* MabiAuto runtime counters.
        // Empty fields remain empty: no mock DPS, fake chat or game-state claims.
        private void PaintMobilayHome(Graphics g)
        {
            TextAt(g, "대시보드 (Dashboard)", new(257, 108, 820, 66), 27, true);
            TextAt(g, "현재 실행 상태와 자동화 기능을 한눈에 확인합니다.",
                new(261, 165, 900, 28), 15, false, Muted);

            void Tile(int column, int row, string caption, string icon)
            {
                int x = 256 + column * 390, y = 210 + row * 364;
                Card(g, new(x, y, 374, 349));
                Icon(g, icon, new(x + 20, y + 19, 24, 24), Blue);
                TextAt(g, caption, new(x + 57, y + 12, 255, 41), 20, true);
                using var pen = new Pen(Line);
                g.DrawLine(pen, x + 18, y + 60, x + 355, y + 60);
            }

            Tile(0, 0, "자동화 상태", "pulse");
            Tile(1, 0, "모드 / 진행 단계", "list");
            Tile(2, 0, "생활 생산", "leaf");
            Tile(0, 1, "실행 통계", "stats");
            Tile(1, 1, "시스템 상태", "server");
            Tile(2, 1, "최근 활동", "log");

            Dot(g, 285, 320, 7, _owner.AnyRunning ? Green : Muted);
            TextAt(g, _owner._statusValue.Text, new(305, 289, 296, 54), 23, true);
            TextAt(g, "경과 시간", new(278, 371, 144, 35), 16, false, Muted);
            TextAt(g, _owner._elapsedValue.Text, new(432, 371, 175, 36),
                17, true, TitleText, StringAlignment.Far);
            TextAt(g, "진행률", new(278, 428, 144, 35), 16, false, Muted);
            TextAt(g, _owner._progress.Value + "%", new(511, 427, 96, 35),
                17, true, Blue, StringAlignment.Far);
            using (var track = new SolidBrush(CardBg2))
            using (var shape = Round(new(278, 484, 330, 13), 6))
                g.FillPath(track, shape);
            if (_owner._progress.Value > 0)
            {
                using var fill = new SolidBrush(Blue);
                using var shape = Round(new(278, 484,
                    Math.Max(13, 330 * _owner._progress.Value / 100f), 13), 6);
                g.FillPath(fill, shape);
            }

            TextAt(g, "선택 모드", new(667, 290, 321, 31), 15, false, Muted);
            TextAt(g, _owner._currentModeValue.Text, new(667, 324, 321, 54), 23, true);
            TextAt(g, "현재 작업", new(667, 391, 318, 30), 15, false, Muted);
            TextAt(g, _owner._currentDungeonValue.Text, new(667, 422, 322, 43), 17, true);
            TextAt(g, _owner._currentStepValue.Text, new(667, 479, 323, 57),
                15, false, Muted, multiline: true);

            // Recipe list is not queried on the dashboard. This indicates
            // navigation readiness, NOT the availability of game facilities.
            string[] production = {
                "가죽 가공", "금속 가공", "목재 가공",
                "식재료 가공", "옷감 가공", "채집 / 제작"
            };
            for (int i = 0; i < production.Length; i++)
            {
                int y = 283 + i * 42;
                TextAt(g, production[i], new(1057, y, 209, 36), 16);
                TextAt(g, "설정에서 선택", new(1262, y, 133, 36),
                    14, false, Muted, StringAlignment.Far);
            }

            TextAt(g, "완료 / 시도", new(278, 650, 306, 33), 16, false, Muted);
            TextAt(g, _owner._successValue.Text + " / " + _owner._roundValue.Text,
                new(278, 692, 316, 62), 28, true);
            TextAt(g, "성공률", new(278, 778, 130, 35), 16, false, Muted);
            TextAt(g, _owner._rateValue.Text, new(414, 778, 178, 35),
                21, true, Green, StringAlignment.Far);
            TextAt(g, "통계는 실제 기록만 표시됩니다.", new(278, 843, 316, 35),
                14, false, Muted);

            var checks = new (string Name, Label Value)[]
            {
                ("게임 연결", _owner._sysGameValue),
                ("화면 인식", _owner._sysOcrValue),
                ("입력 장치", _owner._sysInputValue),
                ("감시기", _owner._sysWatchdogValue),
                ("업데이트", _owner._sysUpdateValue)
            };
            for (int i = 0; i < checks.Length; i++)
            {
                int y = 649 + i * 44;
                TextAt(g, checks[i].Name, new(667, y, 169, 36), 16, false, Muted);
                TextAt(g, checks[i].Value.Text, new(827, y, 171, 36),
                    14, true, TitleText, StringAlignment.Far);
            }

            TextAt(g, "최근 인식", new(1056, 651, 321, 34),
                16, false, Muted);
            TextAt(g, _owner._recentDetectValue.Text, new(1056, 689, 320, 54),
                17, true, TitleText, multiline: true);
            using (var pen = new Pen(Line))
                g.DrawLine(pen, 1056, 758, 1390, 758);
            var activity = string.IsNullOrWhiteSpace(_owner._progressLogPreview.Text)
                ? "최근 활동 기록이 없습니다." : _owner._progressLogPreview.Text;
            TextAt(g, activity, new(1056, 776, 322, 108),
                15, false, Muted, multiline: true);
        }
    }
}
