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
            AddButton("홈", new(16, 123, 200, 49), () =>
            {
                _showHome = true;
                _selectedNavigation = "홈";
                owner._productionPageMode = null;
                owner._logExpanded = false;
                owner.ApplyLogVisibility();
                UpdateAbyssDungeonPickerVisibility();
            }, "home", "nav");
            AddButton("자동화 실행", new(16, 181, 200, 49), () => SelectMode(0), "play", "nav");
            AddButton("던전 / 어비스", new(16, 239, 200, 49), () => ShowMenu(
                new[] { "던전", "어비스" },
                i => SelectMode(new[] { 1, 2 }[i]), 228, 239), "swords", "nav");
            AddButton("생활 생산", new(16, 297, 200, 49), () => ShowMenu(
                new[] { "자동 가공", "자동 채집", "제작" },
                i => SelectMode(new[] { 4, 5, 6 }[i]), 228, 297), "leaf", "nav");
            AddButton("스케줄러", new(16, 355, 200, 49), () => SelectMode(7), "calendar-clock", "nav");
            AddButton("기타 / 설정", new(16, 413, 200, 49), () => ShowMenu(
                new[] { "텔레그램 알림", "오류 전송", "인식 테스트" }, i =>
                {
                    if (i == 0) owner.ShowTelegramSettings();
                    else if (i == 1) owner.ShowErrorUploadSettings();
                    else owner.ShowVisualRecognitionTest();
                }, 228, 413), "settings", "nav");
            AddButton("로그 / 진단", new(16, 471, 200, 49), () =>
            {
                owner._logExpanded = true;
                owner.ApplyLogVisibility();
                UpdateAbyssDungeonPickerVisibility();
            }, "log", "nav");

            // Mobilay-style bottom sidebar controls. No gameplay, network,
            // packet or automated repair is performed by presentation actions.
            AddButton("탭 접기", new(16, 822, 200, 40), () => owner.ToggleMini(true), "", "quiet");
            AddButton("창 최소화", new(16, 869, 200, 40),
                () => owner.WindowState = FormWindowState.Minimized, "", "quiet");
            var alwaysTop = AddButton("전체 최상단", new(16, 916, 200, 40),
                () => owner.TopMost = !owner.TopMost, "", "quiet");
            alwaysTop.Click += (_, _) =>
                alwaysTop.Text = owner.TopMost ? "최상단 해제" : "전체 최상단";
            AddButton("설정", new(16, 963, 200, 40), () => ShowSettingsMenu(228, 963),
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
                int x = 256 + col * 390, y = 198 + row * 315;
                _homeLinks.Add(AddButton("카드 열기 " + (i + 1),
                    new(x + 328, y + 12, 33, 33), targets[i],
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
        // More compact 3x2 dashboard, plus a narrow live-activity strip.
        // Coordinates are virtual (1448x1086); no mouse/game input in paint.
        private void PaintMobilayHome(Graphics g)
        {
            TextAt(g, "대시보드", new(257, 113, 620, 50), 26, true);
            TextAt(g, "작업 상태 · 생산 · 통계 · 진단", new(260, 166, 900, 26), 15, false, Muted);

            void Tile(int column, int row, string caption, string icon)
            {
                int x = 256 + column * 390, y = 198 + row * 315;
                Card(g, new(x, y, 374, 302));
                Icon(g, icon, new(x + 18, y + 15, 24, 24), Blue);
                TextAt(g, caption, new(x + 51, y + 8, 270, 38), 19, true);
                using var pen = new Pen(Line);
                g.DrawLine(pen, x + 15, y + 56, x + 358, y + 56);
            }

            Tile(0, 0, "자동화 상태", "pulse");
            Tile(1, 0, "모드 / 진행 단계", "list");
            Tile(2, 0, "생활 생산", "leaf");
            Tile(0, 1, "실행 통계", "stats");
            Tile(1, 1, "시스템 상태", "server");
            Tile(2, 1, "최근 활동", "log");

            // Existing observable runtime, not synthetic game/DPS values.
            Dot(g, 284, 300, 7, _owner.AnyRunning ? Green : Muted);
            TextAt(g, _owner._statusValue.Text, new(306, 273, 300, 54), 22, true);
            TextAt(g, "경과 시간", new(278, 340, 144, 36), 16, false, Muted);
            TextAt(g, _owner._elapsedValue.Text, new(428, 340, 181, 36),
                17, true, TitleText, StringAlignment.Far);
            TextAt(g, "진행률", new(278, 393, 145, 32), 16, false, Muted);
            TextAt(g, _owner._progress.Value + "%", new(510, 392, 99, 34),
                17, true, Blue, StringAlignment.Far);
            using (var track = new SolidBrush(CardBg2))
            using (var shape = Round(new(278, 450, 330, 12), 6))
                g.FillPath(track, shape);
            if (_owner._progress.Value > 0)
            {
                using var fill = new SolidBrush(Blue);
                using var shape = Round(new(278, 450,
                    Math.Max(12, 330 * _owner._progress.Value / 100f), 12), 6);
                g.FillPath(fill, shape);
            }

            TextAt(g, "선택 모드", new(667, 272, 320, 30), 15, false, Muted);
            TextAt(g, _owner._currentModeValue.Text, new(667, 302, 320, 43), 22, true);
            TextAt(g, "현재 작업", new(667, 354, 321, 29), 15, false, Muted);
            TextAt(g, _owner._currentDungeonValue.Text, new(667, 384, 323, 40), 17, true);
            TextAt(g, _owner._currentStepValue.Text, new(667, 436, 323, 47),
                15, false, Muted, multiline: true);

            // Labels are navigation hints, not claimed recipe availability.
            string[] production =
            {
                "가죽 가공", "금속 가공", "목재 가공",
                "식재료 가공", "옷감 가공", "채집 / 제작"
            };
            for (int i = 0; i < production.Length; i++)
            {
                int y = 265 + i * 36;
                TextAt(g, production[i], new(1057, y, 198, 32), 15);
                TextAt(g, "메뉴에서 설정", new(1246, y, 144, 32),
                    13, false, Muted, StringAlignment.Far);
            }

            TextAt(g, "완료 / 시도", new(278, 571, 316, 32), 16, false, Muted);
            TextAt(g, _owner._successValue.Text + " / " + _owner._roundValue.Text,
                new(278, 607, 311, 58), 28, true);
            TextAt(g, "성공률", new(278, 692, 132, 34), 16, false, Muted);
            TextAt(g, _owner._rateValue.Text, new(414, 692, 178, 35),
                20, true, Green, StringAlignment.Far);
            TextAt(g, "실제 실행 기록만 표시", new(278, 757, 316, 28),
                13, false, Muted);

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
                int y = 568 + i * 42;
                TextAt(g, checks[i].Name, new(667, y, 170, 34), 15, false, Muted);
                TextAt(g, checks[i].Value.Text, new(835, y, 161, 34),
                    14, true, TitleText, StringAlignment.Far);
            }

            TextAt(g, "최근 인식", new(1056, 565, 321, 33), 15, false, Muted);
            TextAt(g, _owner._recentDetectValue.Text, new(1056, 603, 320, 49),
                16, true, TitleText, multiline: true);
            using (var pen = new Pen(Line))
                g.DrawLine(pen, 1056, 673, 1390, 673);
            var activity = string.IsNullOrWhiteSpace(_owner._progressLogPreview.Text)
                ? "최근 활동 기록이 없습니다." : _owner._progressLogPreview.Text;
            TextAt(g, activity, new(1056, 685, 322, 111),
                15, false, Muted, multiline: true);

            // Compact read-only footer instead of blank padding.
            Card(g, new(256, 829, 1166, 115));
            Icon(g, "log", new(274, 851, 23, 23), Blue);
            TextAt(g, "실행 기록", new(309, 840, 241, 42), 18, true);
            TextAt(g, "F8 테스트  ·  F9 시작  ·  F10 정지", new(962, 845, 433, 34),
                14, false, Muted, StringAlignment.Far);
            TextAt(g, activity, new(277, 890, 831, 40),
                16, false, TitleText);
        }
    }
}
