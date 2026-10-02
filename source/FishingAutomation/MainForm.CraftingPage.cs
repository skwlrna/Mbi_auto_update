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
        private CraftableItem[] _choices = Array.Empty<CraftableItem>();
        private bool _loading;
        private bool _loaded;
        private float _layoutScale = 1;

        internal readonly ComboBox Items = new CraftingCombo();
        internal readonly NumericUpDown Quantity = new ArrowlessNumericUpDown();
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
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(14, 6, 14, 10),
                Margin = Padding.Empty,
                BackColor = WindowBg
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 51));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 49));

            var header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty
            };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            header.Controls.Add(owner.SectionTitle("제작", 26), 0, 0);
            header.Controls.Add(new Label
            {
                Text = "아이템·음식을 나눠 원하는 품목을 자동 제작합니다.",
                Dock = DockStyle.Fill,
                ForeColor = Muted,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            root.Controls.Add(header, 0, 0);

            var settings = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0, 0, 0, 10)
            };
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));

            var left = Card();
            left.Padding = new Padding(16, 9, 16, 9);
            left.Margin = new Padding(0, 0, 9, 0);
            var form = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                BackColor = Color.Transparent
            };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            form.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            for (int i = 1; i < form.RowCount; i++)
                form.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

            var settingsHeading = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            settingsHeading.Controls.Add(new Label
            {
                Text = "제작 설정",
                Dock = DockStyle.Fill,
                ForeColor = TitleText,
                Font = new Font("맑은 고딕", 15f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft
            });
            form.Controls.Add(settingsHeading, 0, 0);
            form.SetColumnSpan(settingsHeading, 2);

            _reload = Button("목록 새로고침", () => _ = LoadCatalogAsync(true));
            _reload.AccessibleName = "제작 목록 새로고침";
            _reload.Dock = DockStyle.Right;
            _reload.Width = 110;
            _reload.Margin = new Padding(2);
            settingsHeading.Controls.Add(_reload);

            var categoryTabs = BuildCategoryTabs();
            form.Controls.Add(categoryTabs, 0, 1);
            form.SetColumnSpan(categoryTabs, 2);

            _search.PlaceholderText = "품목을 검색하세요";
            StyleField(_search);
            AddRow(form, 2, "품목 검색", _search);

            Items.DropDownStyle = ComboBoxStyle.DropDownList;
            StyleField(Items);
            StyleCombo(Items);
            Items.AccessibleName = "제작 품목";
            AddRow(form, 3, "제작 품목", Items);

            Quantity.Minimum = 1;
            Quantity.Maximum = 1_000_000;
            Quantity.Value = 100;
            StyleField(Quantity);
            Quantity.AccessibleName = "제작 목표 수량";
            AddRow(form, 4, "목표 수량", Quantity);

            left.Controls.Add(form);
            settings.Controls.Add(left, 0, 0);

            var summary = Card();
            var summaryLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(16, 14, 16, 14)
            };
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
            summaryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 26));
            summaryLayout.Controls.Add(Label("선택한 제작 품목", 21, true), 0, 0);
            foreach (var label in new[] { _selected, Owned, _detail, _state })
            {
                label.Dock = DockStyle.Fill;
                label.ForeColor = TitleText;
                label.Font = new Font("맑은 고딕", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
                label.TextAlign = ContentAlignment.MiddleLeft;
                label.AutoEllipsis = true;
                summaryLayout.Controls.Add(label);
            }
            summary.Controls.Add(summaryLayout);
            settings.Controls.Add(summary, 1, 0);
            root.Controls.Add(settings, 0, 1);

            var execution = Card();
            var executionLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(16, 12, 16, 12)
            };
            executionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            executionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            executionLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            executionLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            executionLayout.Controls.Add(Label("▶  실행 상태", 21, true), 0, 0);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            _start = Button("▶   제작 시작", owner.StartSelected);
            _start.AccessibleName = "제작 시작";
            _start.BackColor = Accent;
            _stop = Button("■   정지 (F10)", owner.StopSelected);
            _stop.AccessibleName = "제작 정지";
            actions.Controls.Add(_start, 0, 0);
            actions.Controls.Add(_stop, 1, 0);
            executionLayout.Controls.Add(actions, 0, 1);

            _progress.Dock = DockStyle.Fill;
            _progress.ForeColor = Color.FromArgb(36, 195, 255);
            _progress.Font = new Font("맑은 고딕", 20f, FontStyle.Bold, GraphicsUnit.Pixel);
            _progress.TextAlign = ContentAlignment.MiddleLeft;
            executionLayout.Controls.Add(_progress, 0, 2);

            var foot = new Label
            {
                Text = "제작은 최대 10회 퀘스트 · 직접 채집 재료는 퀘스트 추천 경로 · 중간재는 자동 가공",
                Dock = DockStyle.Fill,
                ForeColor = Muted,
                Font = new Font("맑은 고딕", 14f, FontStyle.Regular, GraphicsUnit.Pixel),
                TextAlign = ContentAlignment.MiddleLeft
            };
            executionLayout.Controls.Add(foot, 0, 3);
            execution.Controls.Add(executionLayout);
            root.Controls.Add(execution, 0, 2);

            Controls.Add(root);

            // Match automatic-altering typography and responsive scaling exactly.
            foreach (var control in Descendants(root).Prepend(root))
                control.Font = new Font("맑은 고딕", 18f, control.Font.Style, GraphicsUnit.Pixel);
            void Typography(Control control, float pixels) =>
                control.Font = new Font("맑은 고딕", pixels, control.Font.Style, GraphicsUnit.Pixel);

            Typography(header.Controls[0], 36);
            Typography(header.Controls[1], 18);
            Typography(settingsHeading.Controls[0], 21);
            Typography(summaryLayout.Controls[0], 21);
            Typography(executionLayout.Controls[0], 21);
            Typography(_reload, 14);
            Typography(_selected, 20);
            Typography(Owned, 18);
            Typography(_detail, 17);
            Typography(_state, 17);
            Typography(_start, 22);
            Typography(_stop, 22);
            Typography(_progress, 20);
            Typography(foot, 14);
            Typography(_item, 18);
            Typography(_food, 18);

            var metrics = Descendants(root).Prepend(root)
                .Select(control => new CraftingPageMetric(
                    control,
                    control.Font.FontFamily.Name,
                    control.Font.Size,
                    control.Font.Style,
                    control.Padding,
                    control.Margin))
                .ToArray();
            var rows = metrics.SelectMany(metric => metric.Control is TableLayoutPanel table
                ? table.RowStyles.Cast<RowStyle>()
                    .Where(style => style.SizeType == SizeType.Absolute)
                    .Select(style => (Style: style, Size: style.Height))
                : Enumerable.Empty<(RowStyle Style, float Size)>()).ToArray();
            var columns = metrics.SelectMany(metric => metric.Control is TableLayoutPanel table
                ? table.ColumnStyles.Cast<ColumnStyle>()
                    .Where(style => style.SizeType == SizeType.Absolute)
                    .Select(style => (Style: style, Size: style.Width))
                : Enumerable.Empty<(ColumnStyle Style, float Size)>()).ToArray();
            var scaledFonts = new List<Font>();
            var fontCache = new Dictionary<(string Family, float Size, FontStyle Style), Font>();
            float lastScale = -1;
            int lastDpi = -1;
            bool fitting = false;

            Disposed += (_, _) =>
            {
                foreach (var font in scaledFonts) font.Dispose();
            };

            void FitPage()
            {
                if (fitting || ClientSize.Width < 1 || ClientSize.Height < 1) return;
                float scale = Math.Min(ClientSize.Width / 1004f, ClientSize.Height / 711f);
                float fontScale = Math.Min(ClientSize.Width / 1212f, ClientSize.Height / 858f);
                if (Math.Abs(scale - lastScale) < .001f && DeviceDpi == lastDpi) return;

                lastScale = scale;
                lastDpi = DeviceDpi;
                fitting = true;
                _layoutScale = scale;
                root.SuspendLayout();
                foreach (var metric in metrics)
                {
                    float size = Math.Max(
                        metric.Size >= 18 ? 13f : 12f,
                        MathF.Round(metric.Size * fontScale));
                    var key = (metric.Family, size, metric.Style);
                    if (!fontCache.TryGetValue(key, out var font))
                    {
                        font = new Font(metric.Family, size, metric.Style, GraphicsUnit.Pixel);
                        fontCache.Add(key, font);
                        scaledFonts.Add(font);
                    }

                    metric.Control.Font = font;
                    metric.Control.Padding = ScalePadding(metric.Padding, scale);
                    metric.Control.Margin = ScalePadding(metric.Margin, scale);
                }

                foreach (var row in rows) row.Style.Height = row.Size * scale;
                foreach (var column in columns) column.Style.Width = column.Size * scale;
                _reload.Width = Math.Max(70, (int)(110 * scale));
                foreach (var field in Descendants(root)
                    .Where(control => control.Tag is string tag && tag == "crafting-field"))
                    LayoutField(field);
                root.ResumeLayout(true);
                fitting = false;
            }

            Resize += (_, _) => FitPage();
            DpiChangedAfterParent += (_, _) => FitPage();
            FitPage();

            _search.TextChanged += (_, _) => Filter();
            Items.SelectedIndexChanged += (_, _) => SelectionChanged();
            Quantity.ValueChanged += (_, _) => SelectionChanged();
            SetCategory(CraftingCategory.Food);
            UpdateExecution();
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

        private sealed class ArrowlessNumericUpDown : NumericUpDown
        {
            protected override void OnCreateControl()
            {
                base.OnCreateControl();
                HideArrowButtons();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                HideArrowButtons();
            }

            private void HideArrowButtons()
            {
                foreach (Control child in Controls)
                {
                    string typeName = child.GetType().Name;
                    if (typeName.Contains("UpDownButtons", StringComparison.Ordinal))
                    {
                        child.Visible = false;
                        child.Width = 0;
                    }
                    else if (typeName.Contains("UpDownEdit", StringComparison.Ordinal))
                    {
                        child.Dock = DockStyle.Fill;
                        child.Width = ClientSize.Width;
                    }
                }
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
            if (control is NumericUpDown number) number.BorderStyle = BorderStyle.None;
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
            _progress.Text = $"진행 수량  {current:N0} / {target:N0}   ·   {percent}%";
            _state.Text = ownRun
                ? "현재 상태  " + _owner._statusValue.Text
                : $"CLI {CliStatus} · {(Category == CraftingCategory.Food ? "음식" : "아이템")}";
        }
    }
}
