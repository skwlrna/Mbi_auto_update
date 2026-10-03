using System.Drawing.Drawing2D;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private CraftingPage _craftingPage = null!;
    private bool _craftingPolling;
    private DateTime _craftingNextPoll;

    private sealed record CraftingChoice(CraftableItem Item)
    {
        public override string ToString() => Item.DisplayName;
    }

    private CraftingPlan SelectedCraftingPlan()
    {
        if (_craftingPage.Items.SelectedItem is not CraftingChoice choice)
            throw new InvalidOperationException("제작 품목 목록을 불러온 뒤 품목을 선택하세요.");
        if (_craftingPage.SelectedAmbiguous)
            throw new InvalidOperationException("동일 이름 제작 제법이 여러 개라 자동 선택할 수 없습니다.");
        return new(
            _craftingPage.Category,
            choice.Item.DisplayName,
            (int)_craftingPage.Quantity.Value,
            choice.Item.ProducedPerCraft);
    }

    private async Task RefreshCraftingStateAsync()
    {
        if (_craftingPolling || _craftingPage is null || _craftingPage.IsDisposed ||
            _craftingPage.SelectedName is null || DateTime.UtcNow < _craftingNextPoll)
            return;

        _craftingPolling = true;
        string selected = _craftingPage.SelectedName;
        try
        {
            long count = await new CraftingCliData(_cli).ItemCountAsync(selected, CancellationToken.None);
            if (!IsDisposed && _craftingPage.SelectedName == selected)
            {
                _craftingPage.Owned.Text = $"현재 보유량  {count:N0}개";
                _craftingPage.CliStatus = "정상";
            }
        }
        catch
        {
            if (!IsDisposed)
            {
                _craftingPage.Owned.Text = "조회 실패";
                _craftingPage.CliStatus = "연결 확인 필요";
            }
        }
        finally
        {
            _craftingPolling = false;
            _craftingNextPoll = DateTime.UtcNow.AddSeconds(5);
            if (!IsDisposed) _craftingPage.UpdateExecution();
        }
    }

    private sealed class CraftingPage : Panel
    {
        private readonly MainForm _owner;
        private readonly TextBox _search = new();
        private Button _food = null!;
        private Button _item = null!;
        private readonly Button _start;
        private readonly Button _stop;
        private readonly Button _reload;
        private readonly Label _selected = new();
        private readonly Label _detail = new();
        private readonly Label _progress = new();
        private readonly Label _state = new();
        private readonly Label _stage = new();
        private readonly Label _percentage = new();
        private readonly Label _elapsed = new();
        private readonly Label _errors = new();
        private readonly Panel _progressBar = new();
        private int _percent;
        private CraftableItem[] _choices = Array.Empty<CraftableItem>();
        private bool _loading;
        private bool _loaded;
        private float _layoutScale = 1;

        internal readonly ListBox Items = new();
        internal readonly QuantityTextBox Quantity = new();
        internal readonly Label Owned = new();
        internal CraftingCategory Category { get; private set; } = CraftingCategory.Food;
        internal string CliStatus = "확인 전";
        internal string? SelectedName => Items.SelectedItem is CraftingChoice choice
            ? choice.Item.DisplayName : null;
        internal bool SelectedAmbiguous => SelectedName is string selected &&
            _choices.Count(x => string.Equals(x.DisplayName, selected, StringComparison.Ordinal)) > 1;

        internal CraftingPage(MainForm owner)
        {
            _owner = owner;
            Name = "CraftingPage";
            AccessibleName = "제작";
            BackColor = WindowBg;
            ForeColor = TitleText;
            Visible = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(14, 6, 14, 10), Margin = Padding.Empty, BackColor = WindowBg
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            header.Controls.Add(owner.SectionTitle("제작", 26), 0, 0);
            header.Controls.Add(new Label { Text = "아이템·음식 분류와 검색으로 원하는 제작 품목을 빠르게 찾습니다.", Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
            root.Controls.Add(header, 0, 0);

            Items.AccessibleName = "제작 품목"; StyleListBox(Items);
            _search.PlaceholderText = "품목을 검색하세요"; StyleField(_search);
            Quantity.Minimum = 1; Quantity.Maximum = 1_000_000; Quantity.Value = 100; StyleField(Quantity);
            Quantity.AccessibleName = "제작 목표 수량";

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0, 0, 0, 10), BackColor = Color.Transparent, AccessibleName = "제작 2열 본문"
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

            var leftCard = Card(); leftCard.Padding = new Padding(16, 10, 16, 10); leftCard.Margin = new Padding(0, 0, 9, 0); leftCard.AccessibleName = "제작 품목 영역";
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, BackColor = Color.Transparent };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36)); left.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            var settingsHeading = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
            settingsHeading.Controls.Add(new Label { Text = "제작 설정", Dock = DockStyle.Fill, ForeColor = TitleText, TextAlign = ContentAlignment.MiddleLeft });
            _reload = Button("목록 새로고침", () => _ = LoadCatalogAsync(true));
            _reload.AccessibleName = "제작 목록 새로고침"; _reload.Dock = DockStyle.Right; _reload.Width = 110; _reload.Margin = new Padding(2);
            settingsHeading.Controls.Add(_reload);
            left.Controls.Add(settingsHeading, 0, 0);
            left.Controls.Add(BuildCategoryTabs(), 0, 1);
            left.Controls.Add(BuildLabeledField("품목 검색", _search, "crafting-field"), 0, 2);
            left.Controls.Add(BuildListArea("제작 품목"), 0, 3);
            left.Controls.Add(BuildLabeledField("목표 수량", Quantity, "crafting-field"), 0, 4);
            leftCard.Controls.Add(left); body.Controls.Add(leftCard, 0, 0);

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Margin = Padding.Empty,
                BackColor = Color.Transparent, AccessibleName = "제작 실행 상태"
            };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var statusCard = Card(); statusCard.Padding = new Padding(14, 10, 14, 10); statusCard.Margin = Padding.Empty;
            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Margin = Padding.Empty, BackColor = Color.Transparent };
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 14)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            status.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.Controls.Add(Label("실행 상태", 21, true), 0, 0);

            var progressLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            progressLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); progressLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            progressLine.Controls.Add(_progress, 0, 0); progressLine.Controls.Add(_percentage, 1, 0);
            foreach (var label in new[] { _state, _stage, _progress, _percentage, _elapsed, _errors, _detail })
            {
                label.Dock = DockStyle.Fill; label.ForeColor = TitleText; label.TextAlign = ContentAlignment.MiddleLeft;
                label.AutoEllipsis = true; label.BackColor = Color.Transparent; label.Margin = Padding.Empty;
            }
            _percentage.TextAlign = ContentAlignment.MiddleRight; _progress.ForeColor = _percentage.ForeColor = Color.FromArgb(36, 195, 255);
            _progressBar.Dock = DockStyle.Fill; _progressBar.Margin = new Padding(0, 3, 0, 3); _progressBar.Paint += (_, e) => DrawProgress(e.Graphics);
            status.Controls.Add(_state, 0, 1); status.Controls.Add(_stage, 0, 2); status.Controls.Add(progressLine, 0, 3);
            status.Controls.Add(_progressBar, 0, 4); status.Controls.Add(_elapsed, 0, 5); status.Controls.Add(_errors, 0, 6); status.Controls.Add(_detail, 0, 7);
            statusCard.Controls.Add(status); right.Controls.Add(statusCard, 0, 0);
            body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 1);

            var actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0, 4, 0, 0), BackColor = Color.Transparent, AccessibleName = "제작 하단 시작 정지"
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            _start = Button("▶   제작 시작 (F9)", owner.StartSelected); _start.AccessibleName = "제작 시작"; _start.BackColor = Accent;
            _stop = Button("■   정지 (F10)", owner.StopSelected); _stop.AccessibleName = "제작 정지";
            actions.Controls.Add(_start, 0, 0); actions.Controls.Add(_stop, 1, 0); root.Controls.Add(actions, 0, 2);
            Controls.Add(root);

            foreach (var control in Descendants(root).Prepend(root)) control.Font = new Font("맑은 고딕", 18f, control.Font.Style, GraphicsUnit.Pixel);
            void Typography(Control control, float pixels) => control.Font = new Font("맑은 고딕", pixels, control.Font.Style, GraphicsUnit.Pixel);
            Typography(header.Controls[0], 36); Typography(header.Controls[1], 18); Typography(settingsHeading.Controls[0], 21);
            Typography(status.Controls[0], 21); Typography(_reload, 14);
            Typography(_item, 17); Typography(_food, 17);
            foreach (var label in new[] { _state, _stage, _elapsed, _errors, _detail }) Typography(label, 17);
            Typography(_progress, 18); Typography(_percentage, 18); Typography(_start, 22); Typography(_stop, 22);

            var metrics = Descendants(root).Prepend(root).Select(control => new CraftingPageMetric(control, control.Font.FontFamily.Name, control.Font.Size, control.Font.Style, control.Padding, control.Margin)).ToArray();
            var rows = metrics.SelectMany(metric => metric.Control is TableLayoutPanel table ? table.RowStyles.Cast<RowStyle>().Where(style => style.SizeType == SizeType.Absolute).Select(style => (Style: style, Size: style.Height)) : Enumerable.Empty<(RowStyle Style, float Size)>()).ToArray();
            var columns = metrics.SelectMany(metric => metric.Control is TableLayoutPanel table ? table.ColumnStyles.Cast<ColumnStyle>().Where(style => style.SizeType == SizeType.Absolute).Select(style => (Style: style, Size: style.Width)) : Enumerable.Empty<(ColumnStyle Style, float Size)>()).ToArray();
            var scaledFonts = new List<Font>(); var fontCache = new Dictionary<(string Family, float Size, FontStyle Style), Font>();
            float lastScale = -1; int lastDpi = -1; bool fitting = false;
            Disposed += (_, _) => { foreach (var font in scaledFonts) font.Dispose(); };
            void FitPage()
            {
                if (fitting || ClientSize.Width < 1 || ClientSize.Height < 1) return;
                float scale = Math.Min(ClientSize.Width / 1004f, ClientSize.Height / 711f);
                float fontScale = Math.Min(ClientSize.Width / 1212f, ClientSize.Height / 858f);
                if (Math.Abs(scale - lastScale) < .001f && DeviceDpi == lastDpi) return;
                lastScale = scale; lastDpi = DeviceDpi; fitting = true; _layoutScale = scale; root.SuspendLayout();
                foreach (var metric in metrics)
                {
                    float size = Math.Max(metric.Size >= 18 ? 13f : 12f, MathF.Round(metric.Size * fontScale));
                    var key = (metric.Family, size, metric.Style);
                    if (!fontCache.TryGetValue(key, out var font))
                    { font = new Font(metric.Family, size, metric.Style, GraphicsUnit.Pixel); fontCache.Add(key, font); scaledFonts.Add(font); }
                    metric.Control.Font = font; metric.Control.Padding = ScalePadding(metric.Padding, scale); metric.Control.Margin = ScalePadding(metric.Margin, scale);
                }
                foreach (var x in rows) x.Style.Height = x.Size * scale; foreach (var x in columns) x.Style.Width = x.Size * scale;
                _reload.Width = Math.Max(70, (int)(110 * scale)); Items.ItemHeight = Math.Max(26, Items.Font.Height + Math.Max(7, (int)(8 * scale)));
                foreach (var field in Descendants(root).Where(control => control.Tag is string tag && tag == "crafting-field")) LayoutField(field);
                root.ResumeLayout(true); fitting = false;
            }
            Resize += (_, _) => FitPage(); DpiChangedAfterParent += (_, _) => FitPage(); FitPage();
            _search.TextChanged += (_, _) => Filter(); Items.SelectedIndexChanged += (_, _) => SelectionChanged();
            Quantity.ValueChanged += (_, _) => SelectionChanged(); SetCategory(CraftingCategory.Food); UpdateExecution();
        }

        private TableLayoutPanel BuildCategoryTabs()
        {
            var tabs = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = Color.Transparent,
                AccessibleName = "제작 분류 탭"
            };
            tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            _item = Button("아이템", () => SetCategory(CraftingCategory.Item));
            _food = Button("음식", () => SetCategory(CraftingCategory.Food));
            _item.AccessibleName = "제작 아이템";
            _food.AccessibleName = "제작 음식";
            _item.Margin = new Padding(2);
            _food.Margin = new Padding(2);
            tabs.Controls.Add(_item, 0, 0);
            tabs.Controls.Add(_food, 1, 0);
            return tabs;
        }

        private static Panel Card()
            => new CraftingCard { Dock = DockStyle.Fill, Margin = new Padding(4) };

        private sealed class CraftingCard : Panel
        {
            internal CraftingCard()
            {
                BackColor = CardBg;
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.ResizeRedraw,
                    true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width < 3 || Height < 3) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(SurfaceColor(Parent));
                var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
                using var shape = Rounded(bounds, 12);
                using var fill = new LinearGradientBrush(
                    bounds, Color.FromArgb(7, 35, 61), CardBg2, 60f);
                using var border = new Pen(Color.FromArgb(21, 77, 116));
                e.Graphics.FillPath(fill, shape);
                e.Graphics.DrawPath(border, shape);
            }
        }

        private static Label Label(string text, float size, bool bold = false) => new()
        {
            Text = text,
            Dock = DockStyle.Fill,
            ForeColor = TitleText,
            Font = new Font("맑은 고딕", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel),
            TextAlign = ContentAlignment.MiddleLeft
        };

        private static Button Button(string text, Action action)
        {
            var button = new CraftingButton
            {
                Text = text,
                Dock = DockStyle.Fill,
                BackColor = CardBg2,
                ForeColor = TitleText,
                Font = new Font("맑은 고딕", 18f, FontStyle.Bold, GraphicsUnit.Pixel),
                Margin = new Padding(3),
                Cursor = Cursors.Hand
            };
            button.Click += (_, _) => action();
            return button;
        }

        private sealed class CraftingButton : Button
        {
            internal CraftingButton()
            {
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.OptimizedDoubleBuffer |
                    ControlStyles.AllPaintingInWmPaint,
                    true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width < 3 || Height < 3) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(SurfaceColor(Parent));
                var bounds = new RectangleF(1, 1, Width - 2, Height - 2);
                using var shape = Rounded(bounds, 10);
                bool primary = Enabled && BackColor == Accent;
                using var fill = new LinearGradientBrush(
                    bounds,
                    primary ? Color.FromArgb(4, 165, 255) : CardBg,
                    primary ? Color.FromArgb(0, 86, 226) : CardBg2,
                    90f);
                using var border = new Pen(
                    primary ? Color.FromArgb(0, 207, 255) : Line,
                    primary ? 1.8f : 1f);
                e.Graphics.FillPath(fill, shape);
                e.Graphics.DrawPath(border, shape);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    ClientRectangle,
                    Enabled ? ForeColor : Muted,
                    TextFormatFlags.HorizontalCenter |
                    TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis);
                if (Focused && ShowFocusCues)
                    ControlPaint.DrawFocusRectangle(
                        e.Graphics,
                        Rectangle.Inflate(ClientRectangle, -4, -4));
            }
        }

        private sealed class CraftingCombo : ComboBox
        {
            internal CraftingCombo()
            {
                SetStyle(
                    ControlStyles.UserPaint |
                    ControlStyles.AllPaintingInWmPaint |
                    ControlStyles.OptimizedDoubleBuffer,
                    true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(CardBg2);
                TextRenderer.DrawText(
                    e.Graphics,
                    Text,
                    Font,
                    new Rectangle(4, 0, Math.Max(1, Width - 32), Height),
                    Enabled ? TitleText : Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(
                    e.Graphics,
                    "⌄",
                    Font,
                    new Rectangle(Width - 28, 0, 24, Height),
                    Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                if (Focused)
                    ControlPaint.DrawFocusRectangle(
                        e.Graphics,
                        Rectangle.Inflate(ClientRectangle, -2, -2));
            }

            protected override void OnSelectedIndexChanged(EventArgs e)
            {
                base.OnSelectedIndexChanged(e);
                Invalidate();
            }
        }

        private void DrawProgress(Graphics graphics)
        {
            if (_progressBar.Width < 2 || _progressBar.Height < 2) return;
            graphics.Clear(CardBg); graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0, 0, _progressBar.Width - 1, _progressBar.Height - 1);
            using var path = Rounded(bounds, 7); using var track = new SolidBrush(Line); graphics.FillPath(track, path);
            if (_percent > 0)
            {
                using var filled = Rounded(new RectangleF(0, 0, Math.Max(8, bounds.Width * _percent / 100f), bounds.Height), 7);
                using var fill = new LinearGradientBrush(bounds, Color.FromArgb(25, 209, 255), Accent, 90f); graphics.FillPath(fill, filled);
            }
        }

        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            float d = Math.Min(Math.Min(bounds.Width, bounds.Height), radius * 2);
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color SurfaceColor(Control? control)
        {
            while (control is not null)
            {
                if (control.BackColor.A == 255) return control.BackColor;
                control = control.Parent;
            }
            return WindowBg;
        }

        private static void StyleField(Control control)
        {
            control.Dock = DockStyle.Fill;
            control.BackColor = CardBg2;
            control.ForeColor = TitleText;
            control.Margin = new Padding(3, 5, 3, 5);
            control.Font = new Font("맑은 고딕", 13f);
            if (control is TextBox text) text.BorderStyle = BorderStyle.None;
        }

        private static void StyleListBox(ListBox list)
        {
            list.Dock = DockStyle.Fill; list.BorderStyle = BorderStyle.None; list.BackColor = CardBg2; list.ForeColor = TitleText;
            list.IntegralHeight = false; list.DrawMode = DrawMode.OwnerDrawFixed; list.ItemHeight = 32; list.Margin = Padding.Empty;
            list.DrawItem += (_, e) =>
            {
                if (e.Index < 0) return;
                bool selected = (e.State & DrawItemState.Selected) != 0;
                using var background = new SolidBrush(selected ? AccentSoft : CardBg2);
                e.Graphics.FillRectangle(background, e.Bounds);
                TextRenderer.DrawText(e.Graphics, list.Items[e.Index]?.ToString() ?? "", list.Font, Rectangle.Inflate(e.Bounds, -9, 0),
                    selected ? Color.White : TitleText, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if ((e.State & DrawItemState.Focus) != 0) ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds);
            };
        }

        private Control BuildLabeledField(string caption, Control control, string tag)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Color.Transparent };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty }, 0, 0);
            var field = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(0, 3, 0, 2), Tag = tag };
            var surface = new CraftingCard { Dock = DockStyle.None, BackColor = CardBg2, Margin = Padding.Empty };
            control.Dock = DockStyle.None; surface.Controls.Add(control); field.Controls.Add(surface);
            field.Resize += (_, _) => LayoutField(field); layout.Controls.Add(field, 0, 1); LayoutField(field); return layout;
        }

        private Control BuildListArea(string caption)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Color.Transparent };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = TitleText, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 17f, FontStyle.Bold, GraphicsUnit.Pixel) }, 0, 0);
            var surface = new CraftingCard { Dock = DockStyle.Fill, BackColor = CardBg2, Padding = new Padding(5), Margin = new Padding(0, 2, 0, 2) };
            surface.Controls.Add(Items); layout.Controls.Add(surface, 0, 1); return layout;
        }

        private static void StyleCombo(ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += (_, e) =>
            {
                using var background = new SolidBrush(
                    (e.State & DrawItemState.Selected) != 0 ? AccentSoft : CardBg2);
                e.Graphics.FillRectangle(background, e.Bounds);
                string text = e.Index >= 0
                    ? combo.Items[e.Index]?.ToString() ?? ""
                    : "품목을 선택하세요";
                TextRenderer.DrawText(
                    e.Graphics,
                    text,
                    combo.Font,
                    Rectangle.Inflate(e.Bounds, -5, 0),
                    combo.Enabled ? TitleText : Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }

        private void AddRow(TableLayoutPanel form, int row, string caption, Control control)
        {
            form.Controls.Add(new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);

            var field = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(3, 5, 3, 5),
                Tag = "crafting-field"
            };
            var surface = new CraftingCard
            {
                Dock = DockStyle.None,
                BackColor = CardBg2,
                Margin = Padding.Empty
            };
            control.Dock = DockStyle.None;
            surface.Controls.Add(control);
            field.Controls.Add(surface);
            field.Resize += (_, _) => LayoutField(field);
            form.Controls.Add(field, 1, row);
            LayoutField(field);
        }

        private void LayoutField(Control field)
        {
            var surface = field.Controls[0];
            var control = surface.Controls[0];
            int height = Math.Min(
                field.Height,
                Math.Max(control.Font.Height + 8, (int)(44 * _layoutScale)));
            int inset = Math.Max(4, (int)(11 * _layoutScale));
            surface.Bounds = new Rectangle(
                0,
                Math.Max(0, (field.Height - height) / 2),
                field.Width,
                height);
            control.Width = Math.Max(1, surface.Width - inset * 2);
            control.Location = new Point(
                inset,
                Math.Max(0, (surface.Height - control.Height) / 2));
        }

        private void SetCategory(CraftingCategory category)
        {
            Category = category;
            _food.BackColor = category == CraftingCategory.Food ? Accent : CardBg2;
            _item.BackColor = category == CraftingCategory.Item ? Accent : CardBg2;
            _food.ForeColor = category == CraftingCategory.Food ? Color.White : TitleText;
            _item.ForeColor = category == CraftingCategory.Item ? Color.White : TitleText;
            _food.Invalidate();
            _item.Invalidate();
            Filter();
        }

        internal async Task LoadCatalogAsync(bool force = false)
        {
            if (_loading || (_loaded && !force) || _owner.AnyRunning) return;
            _loading = true;
            UpdateExecution();
            try
            {
                _choices = (await new CraftingCliData(_owner._cli).CatalogAsync(CancellationToken.None)).ToArray();
                _loaded = true;
                CliStatus = "정상";
                Filter();
                _owner._log.Write(
                    $"[제작] CLI 제작 목록 { _choices.Length }개 · 음식/아이템 검색 UI 준비");
                _owner._craftingNextPoll = DateTime.MinValue;
                await _owner.RefreshCraftingStateAsync();
            }
            catch (Exception ex)
            {
                _choices = Array.Empty<CraftableItem>();
                _loaded = false;
                Items.Items.Clear();
                Owned.Text = "조회 실패";
                _detail.Text = "제작 목록을 불러오지 못했습니다.";
                CliStatus = "연결 확인 필요";
                _owner._log.Write("[제작] 목록 조회 실패: " + ex.Message);
            }
            finally
            {
                _loading = false;
                UpdateExecution();
            }
        }

        private void Filter()
        {
            if (Items is null) return;
            string? previous = SelectedName;
            string search = _search.Text.Trim();
            var visible = CraftingQueries.ForUiCategory(_choices, Category)
                .Where(x => string.IsNullOrWhiteSpace(search) ||
                    x.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.DisplayName, StringComparer.Ordinal)
                .Select(x => (object)new CraftingChoice(x))
                .ToArray();

            Items.BeginUpdate();
            Items.Items.Clear();
            Items.Items.AddRange(visible);
            int selected = Items.Items.Cast<object>().ToList().FindIndex(x => x.ToString() == previous);
            if (Items.Items.Count > 0) Items.SelectedIndex = Math.Max(0, selected);
            Items.EndUpdate();
            SelectionChanged();
        }

        private void SelectionChanged()
        {
            _owner._craftingNextPoll = DateTime.MinValue;
            if (Items.SelectedItem is not CraftingChoice choice)
            {
                _selected.Text = "품목을 선택하세요";
                Owned.Text = "—";
                _detail.Text = "—";
                _state.Text = "대기 중";
                UpdateExecution();
                return;
            }

            _selected.Text = choice.Item.DisplayName;
            _detail.Text = SelectedAmbiguous
                ? "동일 이름 제법 여러 개 · 자동 선택 불가"
                : $"1회 {choice.Item.ProducedPerCraft}개 · 배치 최대 10회";
            _state.Text = choice.Item.Craftable || choice.Item.MissingIngredients.Count > 0
                ? "제작 가능/재료 해결 가능"
                : "조건 확인 필요";
            _ = _owner.RefreshCraftingStateAsync();
            UpdateExecution();
        }

        private sealed record CraftingPageMetric(
            Control Control,
            string Family,
            float Size,
            FontStyle Style,
            Padding Padding,
            Padding Margin);

        private static Padding ScalePadding(Padding padding, float scale) => new(
            (int)(padding.Left * scale),
            (int)(padding.Top * scale),
            (int)(padding.Right * scale),
            (int)(padding.Bottom * scale));

        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in Descendants(child))
                    yield return nested;
            }
        }

        internal void UpdateExecution()
        {
            bool ownRun = _owner._activeMode == "제작";
            bool busy = _owner.AnyRunning || _owner._starting;
            bool selected = Items.SelectedItem is CraftingChoice;
            foreach (Control control in new Control[] { Items, Quantity, _search, _reload, _food, _item })
                control.Enabled = !busy && !_loading;
            _start.Enabled = !busy && !_loading && selected && !SelectedAmbiguous;
            _stop.Enabled = ownRun && _owner.AnyRunning;

            long current = ownRun ||
                (_owner._productionLastMode == "제작" && _owner._productionDisplayName == SelectedName)
                ? _owner._productionCurrentQuantity : 0;
            int target = ownRun ? _owner._productionTargetQuantity : (int)Quantity.Value;
            int percent = target > 0 ? (int)Math.Clamp(current * 100 / target, 0, 100) : 0;
            _percent = percent; _progressBar.Invalidate();
            bool ownResult = _owner._productionLastMode == "제작" && _owner._productionDisplayName == SelectedName;
            var startedAt = _owner._dungeonStartedAt; var stoppedAt = _owner._dungeonStoppedAt;
            var elapsed = ownRun && startedAt.HasValue ? DateTime.Now - startedAt.Value
                : ownResult && stoppedAt.HasValue && startedAt.HasValue ? stoppedAt.Value - startedAt.Value : TimeSpan.Zero;
            _state.Text = $"현재 상태  {(ownRun ? "실행 중" : _loading ? "목록 조회 중" : _owner._runError is not null && ownResult ? "오류" : "대기 중")}";
            _stage.Text = $"현재 단계  {(ownRun ? _owner._statusValue.Text : "—")}";
            _progress.Text = $"진행 수량  {current:N0} / {target:N0}"; _percentage.Text = $"진행률  {percent}%";
            _elapsed.Text = $"경과 시간  {elapsed:hh\\:mm\\:ss}"; _errors.Text = $"오류 횟수  {(_owner._runError is null ? 0 : 1)}회";
            _detail.Text = $"CLI 상태  {CliStatus}"; _detail.ForeColor = CliStatus == "정상" ? Green : Muted;
        }
    }
}
