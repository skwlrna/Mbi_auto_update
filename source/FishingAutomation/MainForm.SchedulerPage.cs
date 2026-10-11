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
        private readonly Label _alteringSummary = new();
        private readonly Label _craftingSummary = new();
        private readonly Label _abyssSummary = new();
        private readonly Label _runStatus = new();
        private readonly Button _start;
        private readonly TabControl _tabs = new();

        internal bool UseAltering => _alters.Checked;
        internal bool UseCrafting => _crafts.Checked;
        internal bool UseAbyss => _abyss.Checked;
        internal string AbyssDungeon => _abyssChoice.SelectedItem?.ToString() ?? "허상의 정박지";

        internal SchedulerPage(MainForm owner)
        {
            _owner = owner;
            Name = "SchedulerPage";
            AccessibleName = "통합 스케줄러";
            Dock = DockStyle.None; // Dashboard owns responsive bounds; never cover sidebar.
            Visible = false;
            BackColor = WindowBg;
            ForeColor = TitleText;
            Font = new Font("맑은 고딕", 11f);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = WindowBg, Padding = new Padding(12, 6, 12, 8)
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            header.Controls.Add(owner.SectionTitle("통합 스케줄러", 25), 0, 0);
            header.Controls.Add(Txt("가공 · 제작 · 어비스를 하나의 입력권으로 순차 관리합니다. 상세 제법/음식 검색은 기존 화면 그대로 사용합니다.", Muted, 12), 0, 1);
            root.Controls.Add(header, 0, 0);

            foreach (var summary in new[] { _alteringSummary, _craftingSummary, _abyssSummary })
            {
                summary.ForeColor = TitleText;
                summary.Font = new Font("맑은 고딕", 12f);
                summary.Dock = DockStyle.Fill;
                summary.TextAlign = ContentAlignment.MiddleLeft;
                summary.AutoEllipsis = true;
            }

            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("맑은 고딕", 10f, FontStyle.Bold);
            _tabs.Padding = new Point(16, 8);
            _tabs.Appearance = TabAppearance.Normal;
            _tabs.TabPages.Add(MakeOverview());
            _tabs.TabPages.Add(MakeAltering());
            _tabs.TabPages.Add(MakeCrafting());
            _tabs.TabPages.Add(MakeAbyss());
            _tabs.SelectedIndexChanged += (_, _) => RefreshSnapshot();
            root.Controls.Add(_tabs, 0, 1);

            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = WindowBg };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
            _runStatus.Dock = DockStyle.Fill;
            _runStatus.TextAlign = ContentAlignment.MiddleLeft;
            _runStatus.ForeColor = Muted;
            _runStatus.AutoEllipsis = true;
            footer.Controls.Add(_runStatus, 0, 0);
            _start = ActionButton("▶  스케줄러 시작 (F9)", () => _ = owner.StartSchedulerAsync(), true);
            footer.Controls.Add(_start, 1, 0);
            footer.Controls.Add(ActionButton("■  정지 (F10)", owner.StopSelected), 2, 0);
            root.Controls.Add(footer, 0, 2);
            Controls.Add(root);

            _alters.Checked = true;
            _abyss.Checked = true;
            _crafts.Checked = false;
            _alters.CheckedChanged += (_, _) => RefreshSnapshot();
            _crafts.CheckedChanged += (_, _) => RefreshSnapshot();
            _abyss.CheckedChanged += (_, _) => RefreshSnapshot();

            _abyssChoice.DropDownStyle = ComboBoxStyle.DropDownList;
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
            Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
        };

        private static Button ActionButton(string label, Action run, bool primary = false) 
        {
            var button = new Button
            {
                Text = label, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : PanelBg, ForeColor = TitleText,
                Font = new Font("맑은 고딕", 11f, FontStyle.Bold),
                Cursor = Cursors.Hand, Margin = new Padding(4)
            };
            button.FlatAppearance.BorderColor = Border;
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Txt("실행할 작업 선택", TitleText, 17, true), 0, 0);
            LayoutCheck(_crafts, "1. 제작  ·  선택한 음식 / 아이템 목표 수량까지 작업", layout, 1);
            LayoutCheck(_alters, "2. 다중가공  ·  시설별 다중 작업 등록 / 수령 / 재료 해결", layout, 2);
            LayoutCheck(_abyss, "3. 어비스  ·  가공 대기 중 한 판 실행 후 퇴장 검증", layout, 3);
            layout.Controls.Add(Txt("실행 순서 : 제작 완료 → 가공 등록 → 가공 대기 중 어비스 1판 → 가공 복귀", TitleText, 12), 0, 4);
            layout.Controls.Add(Txt("제작은 현 단계에서 먼저 끝내는 방식입니다. 제작 중 어비스로 전환하지 않습니다.", Muted, 11), 0, 5);
            layout.Controls.Add(Txt("각 탭에서 가공·제작·어비스 설정 확인 후 F9로 시작하세요. F10은 모든 단계의 중단 요청입니다.", Muted, 11), 0, 6);
            tab.Controls.Add(layout);
            return tab;
        }

        private static void LayoutCheck(CheckBox cb, string caption, TableLayoutPanel table, int row)
        {
            cb.Text = caption; cb.Dock = DockStyle.Fill; cb.AutoSize = false;
            cb.CheckAlign = ContentAlignment.MiddleLeft; cb.Padding = new Padding(8);
            cb.ForeColor = TitleText;
            cb.Font = new Font("맑은 고딕", 12f);
            cb.BackColor = PanelBg;
            cb.FlatStyle = FlatStyle.Flat;
            table.Controls.Add(cb, 0, row);
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
            var layout = Vertical(5);
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Txt("병행 어비스 던전 선택", TitleText, 17, true), 0, 0);
            _abyssChoice.Dock = DockStyle.Fill;
            _abyssChoice.Font = new Font("맑은 고딕", 13f);
            layout.Controls.Add(_abyssChoice, 0, 1);
            layout.Controls.Add(_abyssSummary, 0, 2);
            layout.Controls.Add(Txt("1판 종료 후 결과 화면에서 '나가기'를 확인하고, 던전 밖 확인 전에는 가공 입력을 재개하지 않습니다.", Muted, 11), 0, 3);
            layout.Controls.Add(Txt("실게임 결과 화면 사진을 이용한 '나가기' 검증은 아직 완료되지 않았으므로 운영 버전으로 배포하지 않습니다.", Color.Orange, 11), 0, 4);
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

            _abyssSummary.Text = "선택 던전 : " + AbyssDungeon + "  ·  가공 대기 중 1판씩";

            bool running = _owner._schedulerRunning;
            _runStatus.Text = running
                ? "스케줄러 실행 중 · 제작 → 가공 + 어비스 (F10 정지)"
                : "준비 · " + (_crafts.Checked ? "제작 " : "") +
                  (_alters.Checked ? "가공 " : "") +
                  (_abyss.Checked ? "어비스" : "");
            _start.Enabled = !running && !_owner.AnyRunning;
            _crafts.Enabled = _alters.Enabled = _abyss.Enabled = !running;
            _abyssChoice.Enabled = !running;
        }
    }

    private void OpenSchedulerDetail(int modeIndex)
    {
        if (AnyRunning) return;
        _referenceDashboard?.NavigateToMode(modeIndex);
    }
}
