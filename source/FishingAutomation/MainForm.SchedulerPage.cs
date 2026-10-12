using System.Drawing.Drawing2D;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private SchedulerPage _schedulerPage = null!;

    // This is UI state, not a second production engine. Existing altering and
    // crafting pages are the source of truth for their searchable selections.
    private sealed class SchedulerPage : Panel
    {
        private readonly MainForm _owner;
        private readonly CheckBox _alters = new();
        private readonly CheckBox _crafts = new();
        private readonly CheckBox _abyss = new();
        private readonly ComboBox _abyssChoice = new();
        private readonly ComboBox _minimumWaitChoice = new();
        private readonly Label _alteringSummary = new();
        private readonly Label _craftingSummary = new();
        private readonly Label _abyssSummary = new();
        private readonly Label _runStatus = new();
        private string? _lastStartFeedback;
        internal string? LastStartFeedback => _lastStartFeedback;

        // Keep failed F9/button preflight visible across the 1-second UI refresh.
        internal void ReportStartFeedback(string message)
        {
            _lastStartFeedback = message;
            _runStatus.Text = message;
            _runStatus.ForeColor = Color.DarkOrange;
            _runStatus.AccessibleDescription = message;
            _runStatus.Invalidate();
        }

        private void ClearStartFeedback() => _lastStartFeedback = null;
        private readonly Button _start;
        private readonly TabControl _tabs = new();

        // Keep the native tab painter; manually overpainting its window frame
        // erased inactive tab labels until hover refreshed the screen.
        internal bool UseAltering => _alters.Checked;
        internal bool UseCrafting => _crafts.Checked;
        internal bool UseAbyss => _abyss.Checked;
        internal string AbyssDungeon => _abyssChoice.SelectedItem?.ToString() ?? "허상의 정박지";
        internal long MinimumAbyssWaitSeconds => _minimumWaitChoice.SelectedIndex switch
        {
            0 => 90, 1 => 120, 2 => 180, 3 => 240, 4 => 300,
            _ => 120
        };

        internal SchedulerPage(MainForm owner)
        {
            _owner = owner;
            Name = "SchedulerPage";
            AccessibleName = "통합 스케줄러";
            Dock = DockStyle.None; // Dashboard owns responsive bounds; never cover sidebar.
            Visible = false;
            BackColor = WindowBg;
            ForeColor = TitleText;
            Font = new Font("맑은 고딕", 17f, FontStyle.Regular, GraphicsUnit.Pixel);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = WindowBg, Padding = new Padding(18, 10, 18, 14)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            var heading = owner.SectionTitle("스케줄러", 20);
            heading.Font = new Font("맑은 고딕", 26f, FontStyle.Bold, GraphicsUnit.Pixel);
            header.Controls.Add(heading, 0, 0);
            header.Controls.Add(Txt("제작 → 가공 → 대기 중 어비스를 순서대로 실행합니다. 시작 전 각 작업을 확인하세요.", Muted, 12), 0, 1);
            root.Controls.Add(header, 0, 0);

            foreach (var summary in new[] { _alteringSummary, _craftingSummary, _abyssSummary })
            {
                summary.ForeColor = TitleText;
                summary.Font = new Font("맑은 고딕", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
                summary.Dock = DockStyle.Fill;
                summary.TextAlign = ContentAlignment.MiddleLeft;
                summary.AutoEllipsis = true;
            }

            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("맑은 고딕", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.SizeMode = TabSizeMode.Fixed;
            _tabs.ItemSize = new Size(152, 49);
            _tabs.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = _tabs.SelectedIndex == e.Index;
                using var fill = new SolidBrush(selected ? AccentSoft : CardBg);
                using var edge = new Pen(selected ? Blue : Border);
                e.Graphics.FillRectangle(fill, e.Bounds);
                e.Graphics.DrawRectangle(edge, e.Bounds.X, e.Bounds.Y, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, _tabs.Font,
                    e.Bounds, selected ? Color.FromArgb(22, 58, 112) : TitleText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
            _tabs.Padding = new Point(16, 8);
            _tabs.Appearance = TabAppearance.Normal;
            _minimumWaitChoice.DropDownStyle = ComboBoxStyle.DropDownList;
            _minimumWaitChoice.AccessibleName = "어비스 진입 최소 가공 남은 시간";
            _minimumWaitChoice.BackColor = PanelBg;
            _minimumWaitChoice.ForeColor = TitleText;
            _minimumWaitChoice.FlatStyle = FlatStyle.Flat;
            _minimumWaitChoice.Items.AddRange(new object[] { "90초", "120초", "180초", "240초", "300초" });
            _minimumWaitChoice.SelectedIndex = 1;
            _minimumWaitChoice.SelectedIndexChanged += (_, _) => RefreshSnapshot();

            _tabs.TabPages.Add(MakeOverview());
            _tabs.TabPages.Add(MakeAltering());
            _tabs.TabPages.Add(MakeCrafting());
            _tabs.TabPages.Add(MakeAbyss());
            _tabs.SelectedIndexChanged += (_, _) => { _tabs.Invalidate(); RefreshSnapshot(); };
            root.Controls.Add(_tabs, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = Border, Padding = new Padding(1), Margin = new Padding(0, 2, 0, 0) };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            _runStatus.Dock = DockStyle.Fill;
            _runStatus.AccessibleName = "스케줄러 실행 안내";
            _runStatus.Font = new Font("맑은 고딕", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            _runStatus.TextAlign = ContentAlignment.MiddleLeft;
            _runStatus.ForeColor = Muted;
            _runStatus.AutoEllipsis = false;
            _runStatus.BackColor = CardBg;
            _runStatus.Padding = new Padding(10, 3, 6, 3);
            _runStatus.Margin = new Padding(2, 5, 5, 5);
            footer.Controls.Add(_runStatus, 0, 0);
            _start = ActionButton("▶  스케줄러 시작 (F9)", () => _ = owner.StartSchedulerAsync(), true);
            footer.Controls.Add(_start, 1, 0);
            footer.Controls.Add(ActionButton("■  정지 (F10)", owner.StopSelected), 2, 0);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);

            _alters.Checked = true;
            _abyss.Checked = true;
            _crafts.Checked = false;
            _alters.CheckedChanged += (_, _) => { ClearStartFeedback(); RefreshSnapshot(); };
            _crafts.CheckedChanged += (_, _) => { ClearStartFeedback(); RefreshSnapshot(); };
            _abyss.CheckedChanged += (_, _) => { ClearStartFeedback(); RefreshSnapshot(); };

            _abyssChoice.DropDownStyle = ComboBoxStyle.DropDownList;
            _abyssChoice.AccessibleName = "스케줄러 어비스 던전";
            _abyssChoice.BackColor = PanelBg;
            _abyssChoice.ForeColor = TitleText;
            _abyssChoice.FlatStyle = FlatStyle.Flat;
            _abyssChoice.Items.AddRange(new object[] { "허상의 정박지", "광기의 동굴", "흩어진 물길" });
            _abyssChoice.SelectedIndex = Math.Max(0, owner._abyssDungeon.SelectedIndex);
            _abyssChoice.SelectedIndexChanged += (_, _) =>
            {
                if (_owner.AnyRunning) return;
                _owner._abyssDungeon.SelectedIndex = _abyssChoice.SelectedIndex;
                RefreshSnapshot();
            };
            RefreshSnapshot();
        }

        private static Label Txt(string message, Color color, float size = 13, bool bold = false) => new()
        {
            Text = message, ForeColor = color, Dock = DockStyle.Fill,
            Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };

        private static Button ActionButton(string label, Action run, bool primary = false) 
        {
            var button = new Button
            {
                Text = label, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : CardBg2, ForeColor = primary ? Color.White : TitleText,
                Font = new Font("맑은 고딕", 17f, FontStyle.Bold, GraphicsUnit.Pixel),
                Cursor = Cursors.Hand, Margin = new Padding(4)
            };
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(29, 93, 209) : AccentSoft;
            button.Click += (_, _) => run();
            return button;
        }

        private static TableLayoutPanel Vertical(int rows)
        {
            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = rows,
                BackColor = WindowBg, Padding = new Padding(10), AutoScroll = true
            };
            return table;
        }

        private static TabPage Tab(string title)
        {
            var tab = new TabPage(title) { BackColor = WindowBg, ForeColor = TitleText };
            return tab;
        }

        private TabPage MakeOverview()
        {
            var tab = Tab("작업 계획");
            var layout = Vertical(7);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Txt("실행 순서 및 사용 설정", TitleText, 20, true), 0, 0);
            LayoutCheck(_crafts, "1. 제작  ·  선택한 음식 / 아이템 목표 수량까지 작업", layout, 1);
            LayoutCheck(_alters, "2. 다중가공  ·  시설별 다중 작업 등록 / 수령 / 재료 해결", layout, 2);
            LayoutCheck(_abyss, "3. 어비스  ·  가공 대기 중 한 판 실행 후 퇴장 검증", layout, 3);
            layout.Controls.Add(BuildSequencePreview(), 0, 4);
            layout.Controls.Add(Txt("제작은 먼저 완료하며, 제작 중 어비스로 전환하지 않습니다.", Muted, 15), 0, 5);
            layout.Controls.Add(Txt("각 작업 설정을 확인한 뒤 F9 시작 · F10 정지", Muted, 15), 0, 6);
            tab.Controls.Add(layout);
            return tab;
        }

        // Read-only preview of the existing scheduler order; no execution state is owned here.
        private static Control BuildSequencePreview()
        {
            var flow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
                Margin = new Padding(0, 6, 0, 6), BackColor = WindowBg,
                AccessibleName = "스케줄러 실행 순서"
            };
            string[] steps = { "1  제작 완료", "2  가공 등록", "3  대기 중 어비스", "4  가공 복귀" };
            for (int i = 0; i < steps.Length; i++)
            {
                flow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
                flow.Controls.Add(new Label
                {
                    Text = steps[i], Dock = DockStyle.Fill, BackColor = AccentSoft,
                    ForeColor = TitleText, TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("맑은 고딕", 16f, FontStyle.Bold, GraphicsUnit.Pixel),
                    Margin = new Padding(3), AutoEllipsis = false
                }, i, 0);
            }
            return flow;
        }

        private static void LayoutCheck(CheckBox cb, string caption, TableLayoutPanel table, int row)
        {
            // One bordered card per step; wrapped labels must not disappear into
            // the nearly identical surrounding white surfaces.
            var edge = new Panel
            {
                Dock = DockStyle.Fill, Margin = new Padding(2, 3, 2, 3),
                Padding = new Padding(1), BackColor = Border
            };
            cb.Text = caption;
            cb.Dock = DockStyle.Fill;
            cb.AutoSize = false;
            cb.CheckAlign = ContentAlignment.MiddleLeft;
            cb.Padding = new Padding(12, 3, 8, 3);
            cb.ForeColor = TitleText;
            cb.Font = new Font("맑은 고딕", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
            cb.BackColor = CardBg;
            cb.FlatStyle = FlatStyle.Flat;
            edge.Controls.Add(cb);
            table.Controls.Add(edge, 0, row);
        }

        private TabPage MakeAltering()
        {
            var tab = Tab("가공 상세");
            var layout = Vertical(5);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Txt("가공 상세 · 원본 작업 목록 연결", TitleText, 17, true), 0, 0);
            layout.Controls.Add(_alteringSummary, 0, 1);
            layout.Controls.Add(Txt("가공시설 6종 · 제법 검색 · 목표 수량 · 다중가공 작업 목록 · 이어하기 기능은 기존 가공 상세화면을 재사용합니다.", Muted, 12), 0, 2);
            layout.Controls.Add(ActionButton("가공 상세 화면에서 품목 검색 / 다중 작업 편집", () => _owner.OpenSchedulerDetail(4), true), 0, 3);
            layout.Controls.Add(Txt("품목을 추가한 뒤 왼쪽 스케줄러 메뉴로 돌아오면 목록이 그대로 유지됩니다.", Muted, 11), 0, 4);
            tab.Controls.Add(layout);
            return tab;
        }

        private TabPage MakeCrafting()
        {
            var tab = Tab("제작 상세");
            var layout = Vertical(5);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Txt("제작 상세 · 음식 / 아이템 검색", TitleText, 17, true), 0, 0);
            layout.Controls.Add(_craftingSummary, 0, 1);
            layout.Controls.Add(Txt("기존 제작 전체 검색, 동일 이름 제법 중복검증, 카테고리 판별과 목표 수량을 그대로 사용합니다.", Muted, 12), 0, 2);
            layout.Controls.Add(ActionButton("제작 상세 화면에서 음식 / 아이템 검색", () => _owner.OpenSchedulerDetail(6), true), 0, 3);
            layout.Controls.Add(Txt("품목과 수량을 설정한 뒤 왼쪽 스케줄러 메뉴로 돌아오세요.", Muted, 11), 0, 4);
            tab.Controls.Add(layout);
            return tab;
        }

        private TabPage MakeAbyss()
        {
            var tab = Tab("어비스 설정");
            var layout = Vertical(7);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Txt("병행 어비스 던전 선택", TitleText, 17, true), 0, 0);
            _abyssChoice.Dock = DockStyle.Fill;
            _abyssChoice.Font = new Font("맑은 고딕", 13f);
            layout.Controls.Add(_abyssChoice, 0, 1);
            layout.Controls.Add(Txt("어비스 출발 기준 · 가공 최소 남은시간 (던전 최대시간이 아님)", TitleText, 12), 0, 2);
            _minimumWaitChoice.Dock = DockStyle.Fill;
            _minimumWaitChoice.Font = new Font("맑은 고딕", 13f);
            layout.Controls.Add(_minimumWaitChoice, 0, 3);
            layout.Controls.Add(_abyssSummary, 0, 4);
            layout.Controls.Add(Txt("어비스 중 가공 관리자 정체 감시만 일시정지합니다. 실제 서버 가공은 계속 진행됩니다. 나가기 → 필드 HUD 확인 뒤 감시를 재개하고 작업 목록을 다시 조회합니다.", Muted, 11), 0, 5);
            layout.Controls.Add(Txt("Windows 실게임에서 고정좌표 클릭 및 던전 밖 HUD 복귀 검증이 필요하므로 운영 버전으로 배포하지 않습니다.", Color.Orange, 11), 0, 6);
            tab.Controls.Add(layout);
            return tab;
        }

        internal void RefreshSnapshot()
        {
            if (IsDisposed || _alteringSummary is null || _craftingSummary is null) return;
            IReadOnlyList<AlteringPlan> plans = _owner._alteringPage?.QueuedAlteringPlans ?? Array.Empty<AlteringPlan>();
            _alteringSummary.Text = plans.Count > 0
                ? "등록 작업 " + plans.Count + "종 : " +
                  string.Join(" / ", plans.Take(5).Select(x => $"{x.DisplayName} {x.TargetQuantity:N0}개")) +
                  (plans.Count > 5 ? $" 외 {plans.Count - 5}종" : "")
                : "다중 작업 목록이 비어 있습니다. 가공 상세에서 품목을 선택하고 작업에 추가하세요.\n" +
                  "단일 선택 품목은 시작 시 한 개 작업으로 사용합니다.";

            _craftingSummary.Text = _owner._craftingPage?.SelectedName is string recipe
                ? $"제작 품목 : {recipe}  ·  목표 {_owner._craftingPage.Quantity.Value:N0}개"
                : "제작 상세에서 음식 또는 아이템을 검색하고 선택하세요.";

            _abyssSummary.Text = "선택 던전 : " + AbyssDungeon + "  ·  출발 기준 " +
                MinimumAbyssWaitSeconds + "초 · 어비스 종료/필드 복귀 시 가공 재조회";

            bool running = _owner._schedulerRunning;
            if (running) ClearStartFeedback();
            _runStatus.Text = running
                ? "스케줄러 실행 중 · 제작 → 가공 + 어비스 (F10 정지)"
                : _lastStartFeedback ?? ("준비 · " + (_crafts.Checked ? "제작 " : "") +
                  (_alters.Checked ? "가공 " : "") +
                  (_abyss.Checked ? "어비스" : ""));
            _runStatus.ForeColor = _lastStartFeedback is not null ? Color.DarkOrange : Muted;
            // Allow an attempted click even if a different runner holds input;
            // the guarded handler explains why it cannot start. Never run twice.
            _start.Enabled = !running;
            _crafts.Enabled = _alters.Enabled = _abyss.Enabled = !running;
            _abyssChoice.Enabled = !running;
            _minimumWaitChoice.Enabled = !running;
        }
    }

    private void OpenSchedulerDetail(int modeIndex)
    {
        if (AnyRunning) return;
        _referenceDashboard?.NavigateToMode(modeIndex);
    }
}
