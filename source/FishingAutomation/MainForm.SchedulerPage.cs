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
        private readonly TabControl _tabs = new();
        private int _hoveredTab = -1;

        // The real WinForms TabControl remains the page/keyboard navigator, but
        // its native header dividers and dark connecting line must be covered by
        // one opaque painted card strip above the content.
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            var heading = owner.SectionTitle("스케줄러", 20);
            heading.Font = new Font("맑은 고딕", 26f, FontStyle.Bold, GraphicsUnit.Pixel);
            header.Controls.Add(heading, 0, 0);
            header.Controls.Add(Txt("제작 → 가공 → 대기 중 어비스를 순서대로 실행합니다. 시작 전 각 작업을 확인하세요.", Muted, 17), 0, 1);
            root.Controls.Add(header, 0, 0);

            foreach (var summary in new[] { _alteringSummary, _craftingSummary, _abyssSummary })
            {
                summary.ForeColor = TitleText;
                summary.Font = new Font("맑은 고딕", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
                summary.Dock = DockStyle.Fill;
                summary.TextAlign = ContentAlignment.MiddleLeft;
                summary.AutoEllipsis = false;
            }

            _tabs.Dock = DockStyle.Fill;
            _tabs.Font = new Font("맑은 고딕", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
            _tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
            _tabs.SizeMode = TabSizeMode.Fixed;
            _tabs.HotTrack = false; // Hover must never imply selection or enablement.
            _tabs.ItemSize = new Size(152, 49);

            // WinForms draws native dark separators OUTSIDE DrawItem bounds,
            // including gaps between owner-drawn cards. Keep the real TabControl
            // underneath for page layout / keyboard switching, and put an opaque,
            // buffered card strip in front of its entire native header + seam.
            var tabHost = new Panel { Dock = DockStyle.Fill, BackColor = WindowBg };
            var tabHeader = new BufferedTabHeader
            {
                Name = "SchedulerTabCardHeader",
                AccessibleName = "스케줄러 카드 헤더",
                BackColor = WindowBg,
                TabStop = false,
                Cursor = Cursors.Hand
            };

            void InvalidateTabHeader() { tabHeader.Invalidate(); _tabs.Invalidate(); }

            void FitTabHeader()
            {
                if (_tabs.TabCount == 0 || tabHost.ClientSize.Width <= 0) return;
                // GetTabRect includes the actual DPI-scaled native button height.
                // Cover an additional six pixels so the dark native horizontal
                // separator at the beginning of the page is not exposed.
                int bottom = Math.Max(_tabs.GetTabRect(0).Bottom + 6,
                    _tabs.DisplayRectangle.Top + 6);
                var headerBounds = new Rectangle(0, 0, tabHost.ClientSize.Width,
                    Math.Min(tabHost.ClientSize.Height, bottom));
                if (tabHeader.Bounds != headerBounds) tabHeader.Bounds = headerBounds;
                tabHeader.BringToFront();
                tabHeader.Invalidate();
            }

            tabHeader.Paint += (_, e) =>
            {
                if (_tabs.TabCount == 0) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                for (int index = 0; index < _tabs.TabCount; index++)
                {
                    Rectangle rect = _tabs.GetTabRect(index);
                    var card = new RectangleF(rect.X + 4, rect.Y + 3,
                        Math.Max(1, rect.Width - 8), Math.Max(1, rect.Height - 6));
                    bool selected = _tabs.SelectedIndex == index;
                    bool hovered = !selected && _hoveredTab == index;
                    using var shape = RoundedTabCard(card, 7);
                    using (var fill = new SolidBrush(selected ? AccentSoft :
                        hovered ? CardBg2 : CardBg))
                        e.Graphics.FillPath(fill, shape);
                    using (var edge = new Pen(selected ? Blue : Border,
                        selected ? 1.7f : 1f))
                        e.Graphics.DrawPath(edge, shape);
                    using var font = new Font(_tabs.Font,
                        selected ? FontStyle.Bold : FontStyle.Regular);
                    TextRenderer.DrawText(e.Graphics, _tabs.TabPages[index].Text, font,
                        Rectangle.Round(card), selected ? Blue : TitleText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
            };
            tabHeader.MouseMove += (_, e) =>
            {
                int hovered = -1;
                for (int index = 0; index < _tabs.TabCount; index++)
                {
                    if (!_tabs.GetTabRect(index).Contains(e.Location)) continue;
                    hovered = index;
                    break;
                }
                if (_hoveredTab == hovered) return;
                _hoveredTab = hovered;
                InvalidateTabHeader();
            };
            tabHeader.MouseLeave += (_, _) =>
            {
                if (_hoveredTab == -1) return;
                _hoveredTab = -1;
                InvalidateTabHeader();
            };
            tabHeader.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                for (int index = 0; index < _tabs.TabCount; index++)
                {
                    if (!_tabs.GetTabRect(index).Contains(e.Location)) continue;
                    _tabs.SelectedIndex = index;
                    _tabs.Focus();
                    return;
                }
            };
            tabHost.Controls.Add(_tabs);
            tabHost.Controls.Add(tabHeader);
            tabHeader.BringToFront();
            tabHost.SizeChanged += (_, _) => FitTabHeader();

            _tabs.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = _tabs.SelectedIndex == e.Index;
                bool hovered = !selected && _hoveredTab == e.Index;
                // The same rounded white / pale-blue selection cards as the
                // 낚시 · 던전 · 어비스 mode buttons. The native tab page stays
                // in place, preserving keyboard navigation and all four pages.
                var card = new RectangleF(e.Bounds.X + 4, e.Bounds.Y + 3,
                    Math.Max(1, e.Bounds.Width - 8), Math.Max(1, e.Bounds.Height - 6));
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (var canvas = new SolidBrush(WindowBg))
                    e.Graphics.FillRectangle(canvas, e.Bounds);
                using var shape = RoundedTabCard(card, 7);
                using (var fill = new SolidBrush(selected ? AccentSoft :
                    hovered ? CardBg2 : CardBg))
                    e.Graphics.FillPath(fill, shape);
                using (var edge = new Pen(selected ? Blue : Border,
                    selected ? 1.7f : 1f))
                    e.Graphics.DrawPath(edge, shape);
                using var font = new Font(_tabs.Font,
                    selected ? FontStyle.Bold : FontStyle.Regular);
                TextRenderer.DrawText(e.Graphics, _tabs.TabPages[e.Index].Text, font,
                    Rectangle.Round(card), selected ? Blue : TitleText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            };
            _tabs.MouseMove += (_, e) =>
            {
                int hovered = -1;
                for (int index = 0; index < _tabs.TabCount; index++)
                {
                    if (!_tabs.GetTabRect(index).Contains(e.Location)) continue;
                    hovered = index;
                    break;
                }
                if (hovered == _hoveredTab) return;
                _hoveredTab = hovered;
                InvalidateTabHeader();
            };
            _tabs.MouseLeave += (_, _) =>
            {
                if (_hoveredTab == -1) return;
                _hoveredTab = -1;
                InvalidateTabHeader();
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
            _tabs.SelectedIndexChanged += (_, _) =>
            {
                InvalidateTabHeader();
                RefreshSnapshot();
            };
            root.Controls.Add(tabHost, 0, 1);

            // The shared full-width button below the page already starts/stops the
            // selected scheduler. Keep one visible action instead of two F9 buttons.
            var footer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1,
                BackColor = WindowBg, Margin = new Padding(0, 2, 0, 0)
            };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _runStatus.Dock = DockStyle.Fill;
            _runStatus.AccessibleName = "스케줄러 실행 안내";
            _runStatus.Font = new Font("맑은 고딕", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            _runStatus.TextAlign = ContentAlignment.MiddleLeft;
            _runStatus.ForeColor = TitleText;
            _runStatus.AutoEllipsis = false;
            _runStatus.BackColor = CardBg;
            _runStatus.Padding = new Padding(10, 3, 6, 3);
            _runStatus.Margin = new Padding(3);
            footer.Controls.Add(_runStatus, 0, 0);
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
            // Use the exact same font and layout scaling rules as automatic
            // altering/crafting. Snapshot design metrics once, never repeatedly
            // shrink already-scaled controls after a resize or DPI change.
            var typography = Descendants(root).Prepend(root).ToArray();
            foreach (Control control in typography)
                control.Font = new Font("맑은 고딕", 18f, control.Font.Style, GraphicsUnit.Pixel);
            void Typography(Control control, float pixels) =>
                control.Font = new Font("맑은 고딕", pixels, control.Font.Style, GraphicsUnit.Pixel);
            Typography(heading, 36);
            Typography(header.GetControlFromPosition(0, 1)!, 18);
            Typography(_tabs, 17);
            Typography(_runStatus, 17);
            foreach (var summary in new[] { _alteringSummary, _craftingSummary, _abyssSummary })
                Typography(summary, 18);
            foreach (var cb in new[] { _crafts, _alters, _abyss }) Typography(cb, 18);
            foreach (var cb in new[] { _abyssChoice, _minimumWaitChoice }) Typography(cb, 18);
            foreach (var label in typography.OfType<Label>())
            {
                if (label == heading || label == _runStatus ||
                    label == _alteringSummary || label == _craftingSummary || label == _abyssSummary) continue;
                if (label.Font.Style.HasFlag(FontStyle.Bold) && label.Text is
                    "실행 순서 및 사용 설정" or "병행 어비스 던전 선택" or
                    "가공 상세 · 원본 작업 목록 연결" or "제작 상세 · 음식 / 아이템 검색")
                    Typography(label, 21);
            }
            foreach (var button in typography.OfType<Button>()) Typography(button, 19);

            var metrics = typography.Select(control => new SchedulerMetric(
                control, control.Font.FontFamily.Name, control.Font.Size,
                control.Font.Style, control.Padding, control.Margin)).ToArray();
            var rows = typography.OfType<TableLayoutPanel>().SelectMany(table =>
                table.RowStyles.Cast<RowStyle>().Where(row => row.SizeType == SizeType.Absolute)
                    .Select(row => (Row: row, Original: row.Height))).ToArray();
            var columns = typography.OfType<TableLayoutPanel>().SelectMany(table =>
                table.ColumnStyles.Cast<ColumnStyle>().Where(col => col.SizeType == SizeType.Absolute)
                    .Select(col => (Column: col, Original: col.Width))).ToArray();
            var fonts = new Dictionary<(string Family, float Size, FontStyle Style), Font>();
            float previousScale = -1, previousFontScale = -1;
            int previousDpi = -1;
            bool fitting = false;
            Disposed += (_, _) => { foreach (var font in fonts.Values) font.Dispose(); };

            void FitPage()
            {
                if (fitting || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
                float layoutScale = Math.Min(ClientSize.Width / 1004f, ClientSize.Height / 711f);
                float fontScale = Math.Min(ClientSize.Width / 1212f, ClientSize.Height / 858f);
                if (Math.Abs(previousScale - layoutScale) < .00001f &&
                    Math.Abs(previousFontScale - fontScale) < .00001f &&
                    previousDpi == DeviceDpi) return;
                previousScale = layoutScale;
                previousFontScale = fontScale;
                previousDpi = DeviceDpi;
                fitting = true;
                root.SuspendLayout();
                try
                {
                    foreach (var metric in metrics)
                    {
                        float pixels = Math.Max(metric.Size >= 18f ? 13f : 12f,
                            MathF.Round(metric.Size * fontScale));
                        var key = (metric.Family, pixels, metric.Style);
                        if (!fonts.TryGetValue(key, out Font? font))
                        {
                            font = new Font(metric.Family, pixels, metric.Style, GraphicsUnit.Pixel);
                            fonts.Add(key, font);
                        }
                        metric.Control.Font = font;
                        metric.Control.Padding = ScalePadding(metric.Padding, layoutScale);
                        metric.Control.Margin = ScalePadding(metric.Margin, layoutScale);
                    }
                    foreach (var row in rows)
                        row.Row.Height = Math.Max(26f, MathF.Round(row.Original * layoutScale));
                    foreach (var col in columns)
                        col.Column.Width = MathF.Round(col.Original * layoutScale);

                    // Fixed WinForms tab headers need explicit scaling. All four
                    // tabs share the available width, and captions never ellipsize.
                    int tabHeight = Math.Max(_tabs.Font.Height + 12, (int)MathF.Round(49f * layoutScale));
                    int tabWidth = Math.Max(95, (_tabs.ClientSize.Width - 8) / 4);
                    var desiredTabs = new Size(tabWidth, tabHeight);
                    if (_tabs.ItemSize != desiredTabs) _tabs.ItemSize = desiredTabs;
                    FitTabHeader();
                    InvalidateTabHeader();
                }
                finally
                {
                    root.ResumeLayout(true);
                    fitting = false;
                }
            }
            Resize += (_, _) => FitPage();
            VisibleChanged += (_, _) => FitPage();
            DpiChangedAfterParent += (_, _) => FitPage();
            bool resizingTabs = false;
            _tabs.Resize += (_, _) =>
            {
                if (!fitting) FitTabWidth();
                FitTabHeader();
            };
            void FitTabWidth()
            {
                if (resizingTabs || _tabs.TabCount == 0 || _tabs.ClientSize.Width <= 0) return;
                var desired = new Size(Math.Max(95, (_tabs.ClientSize.Width - 8) / _tabs.TabCount),
                    _tabs.ItemSize.Height);
                if (_tabs.ItemSize == desired) return;
                // TabControl.ItemSize itself fires Resize on Windows.
                // Prevent an infinite loop while the native tab adjusts its bounds.
                resizingTabs = true;
                try { _tabs.ItemSize = desired; }
                finally { resizingTabs = false; }
                FitTabHeader();
            }
            FitPage();
            RefreshSnapshot();
        }

        private sealed class BufferedTabHeader : Panel
        {
            internal BufferedTabHeader() =>
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer, true);
        }

        private sealed record SchedulerMetric(
            Control Control, string Family, float Size, FontStyle Style,
            Padding Padding, Padding Margin);

        private static Padding ScalePadding(Padding padding, float scale) => new(
            (int)(padding.Left * scale), (int)(padding.Top * scale),
            (int)(padding.Right * scale), (int)(padding.Bottom * scale));

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child)) yield return nested;
            }
        }

        private static Label Txt(string message, Color color, float size = 13, bool bold = false) => new()
        {
            Text = message, ForeColor = color, Dock = DockStyle.Fill,
            Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel),
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false
        };

        private static Button ActionButton(string label, Action run, bool primary = false) 
        {
            var button = new Button
            {
                Text = label, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Blue : CardBg2, ForeColor = primary ? Color.FromArgb(22, 22, 25) : TitleText,
                Font = new Font("맑은 고딕", 17f, FontStyle.Bold, GraphicsUnit.Pixel),
                Cursor = Cursors.Hand, Margin = new Padding(3), TextAlign = ContentAlignment.MiddleCenter,
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderColor = primary ? Blue : Border;
            button.FlatAppearance.BorderSize = 1;
            // Hover changes neither background nor legibility; selected state is
            // conveyed by actual tabs and checkboxes, not the mouse location.
            button.FlatAppearance.MouseOverBackColor = button.BackColor;
            button.FlatAppearance.MouseDownBackColor = button.BackColor;
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

        // Keep these exact proportions aligned with ReferenceButton mode-tab
        // (rounded 7px corners, 1.7px blue selected stroke).
        private static GraphicsPath RoundedTabCard(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
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
                    Font = new Font("맑은 고딕", 17f, FontStyle.Bold, GraphicsUnit.Pixel),
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
            cb.UseVisualStyleBackColor = false;
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
            layout.Controls.Add(Txt("품목 등록·목표 수량은 가공 상세에서 관리합니다. 중단된 다중가공은 스케줄러 시작 시 이어할 수 있습니다.", Muted, 16), 0, 2);
            layout.Controls.Add(ActionButton("가공 상세 화면에서 품목 검색 / 다중 작업 편집", () => _owner.OpenSchedulerDetail(4), true), 0, 3);
            layout.Controls.Add(Txt("품목을 추가하고 스케줄러로 돌아오면 등록 목록이 유지됩니다.", Muted, 16), 0, 4);
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
            layout.Controls.Add(Txt("품목 검색, 동일 이름 제법 구분, 제작 분류와 목표 수량 설정을 그대로 사용합니다.", Muted, 16), 0, 2);
            layout.Controls.Add(ActionButton("제작 상세 화면에서 음식 / 아이템 검색", () => _owner.OpenSchedulerDetail(6), true), 0, 3);
            layout.Controls.Add(Txt("품목과 수량을 설정한 후 스케줄러로 돌아오세요.", Muted, 16), 0, 4);
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
            _abyssChoice.Font = new Font("맑은 고딕", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
            layout.Controls.Add(_abyssChoice, 0, 1);
            layout.Controls.Add(Txt("어비스 출발 기준 · 가공 최소 남은 시간", TitleText, 17), 0, 2);
            _minimumWaitChoice.Dock = DockStyle.Fill;
            _minimumWaitChoice.Font = new Font("맑은 고딕", 17f, FontStyle.Regular, GraphicsUnit.Pixel);
            layout.Controls.Add(_minimumWaitChoice, 0, 3);
            layout.Controls.Add(_abyssSummary, 0, 4);
            layout.Controls.Add(Txt("어비스 중 가공 감시만 일시 정지하며, 복귀 후 작업 목록을 재조회합니다.", Muted, 16), 0, 5);
            layout.Controls.Add(Txt("실제 게임에서 던전 종료 및 복귀 확인이 필요합니다.", Color.DarkOrange, 16), 0, 6);
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
                ? "스케줄러 실행 중 · " + _owner._schedulerMonitor.PhaseLabel + " (F10 정지)"
                : _lastStartFeedback ?? ("준비 · " + (_crafts.Checked ? "제작 " : "") +
                  (_alters.Checked ? "가공 " : "") +
                  (_abyss.Checked ? "어비스" : ""));
            _runStatus.ForeColor = _lastStartFeedback is not null ? Color.DarkOrange : TitleText;
            // Allow an attempted click even if a different runner holds input;
            // the guarded handler explains why it cannot start. Never run twice.
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
