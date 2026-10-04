using System.Drawing.Drawing2D;

namespace FishingAutomation;

public sealed partial class MainForm
{
    private ProductionPage _gatheringPage = null!, _alteringPage = null!;
    private string? _productionPageMode;
    private bool _productionPolling;
    private DateTime _productionNextPoll;
    private long? _productionBaseline;
    private string _productionProgressSummary = "";

    private GatheringPlan SelectedGatheringPlan()
    {
        if (_gatheringPage.Items.SelectedItem is not GatheringChoice choice)
            throw new InvalidOperationException("채집 품목 목록을 불러온 뒤 품목을 선택하세요.");
        if (!choice.Item.ToolOk) throw new InvalidOperationException("채집 도구 상태를 확인하세요.");
        return new(choice.Item.DisplayName, (int)_gatheringPage.Quantity.Value);
    }

    private AlteringPlan SelectedAlteringPlan()
    {
        if (_alteringPage.Items.SelectedItem is not RecipeChoice choice)
            throw new InvalidOperationException("가공 제법 목록을 불러온 뒤 제법을 선택하세요.");

        if (!choice.Recipe.Alterable)
        {
            string reason = choice.Recipe.Reason ?? "unknown";
            string missing = choice.Recipe.MissingIngredients.Count == 0
                ? "없음"
                : string.Join(", ", choice.Recipe.MissingIngredients.Select(x => $"{x.DisplayName} {x.Owned}/{x.Required}"));
            _log.Write($"[자동 가공] 선택 제법 상태 · {choice.Recipe.DisplayName} · Alterable=false · Reason={reason} · Missing={missing}");

            // MissingIngredients is stronger evidence than a localized/changed Reason string.
            // If concrete deficits are present, let the recursive resolver handle them.
            if (choice.Recipe.MissingIngredients.Count == 0)
                throw new InvalidOperationException($"선택한 제법의 가공 조건을 확인하세요. Reason={reason} · Missing={missing}");
        }

        // Missing ingredients are resolved recursively. Spirit Wings are never authorized.
        return new(_alteringPage.Facility.SelectedItem!.ToString()!, choice.Recipe.DisplayName,
            (int)_alteringPage.Quantity.Value, choice.Recipe.ProducedPerWork, false);
    }

    private async Task RefreshProductionStateAsync(ProductionPage page)
    {
        if (_productionPolling || _gatheringDiagnosticRunning) return;
        _productionPolling = true;
        string? selected = page.SelectedName;
        string? facility = page.Facility.SelectedItem?.ToString();
        try
        {
            if (selected is null) return;
            long count = page.IsAltering
                ? await new AlteringCliData(_cli).ItemCountAsync(page.OutputName!, CancellationToken.None)
                : await new GatheringCliData(_cli).ItemCountAsync(selected, CancellationToken.None);
            var identity = CliAutomationGuards.ParseIdentity(await _cli.GetMyInfoAsync());
            int? completed = null;
            if (page.IsAltering)
                completed = (await new AlteringCliData(_cli).WorksAsync(CancellationToken.None))
                    .Count(x => x.IsCompleted && x.FacilityName == facility);
            if (IsDisposed || page.SelectedName != selected || page.Facility.SelectedItem?.ToString() != facility) return;
            page.Owned.Text = $"현재 보유량  {count:N0}개";
            page.CliStatus = "정상";
            page.CharacterStatus = identity.ComparableFields > 0 ? "확인됨" : "확인 필요";
            if (_activeMode == (page.IsAltering ? "가공" : "채집"))
            {
                if (page.IsAltering && _productionBaseline.HasValue)
                    _productionCurrentQuantity = Math.Max(0, count - _productionBaseline.Value);
            }
            page.CompletedWorks = completed;
        }
        catch (Exception)
        {
            if (!IsDisposed)
            {
                page.Owned.Text = "조회 실패";
                page.CliStatus = "연결 확인 필요"; page.CharacterStatus = "확인 필요";
                page.CompletedWorks = null;
            }
        }
        finally
        {
            _productionPolling = false;
            _productionNextPoll = DateTime.UtcNow.AddSeconds(5);
            if (!IsDisposed) page.UpdateExecution();
        }
    }

    private sealed record GatheringChoice(GatherableItem Item)
    { public override string ToString() => Item.DisplayName; }
    private sealed record RecipeChoice(AlteringRecipe Recipe)
    { public override string ToString() => Recipe.DisplayName; }
    private sealed record AlteringQueueChoice(AlteringPlan Plan)
    {
        public override string ToString()
            => $"{Plan.DisplayName}  ·  {Plan.TargetQuantity:N0}개";
    }

    // Native controls in the existing central content region, sharing its palette.
    // Independent pages retain their own selections and never appear together.
    private sealed class ProductionPage : Panel
    {
        private readonly MainForm _owner;
        internal bool IsAltering { get; }
        private readonly Button? _inspect;
        internal readonly ListBox Items = new();
        internal readonly ComboBox Facility = new LauncherCombo();
        internal readonly QuantityTextBox Quantity;
        internal readonly Label Owned = new();
        private readonly TextBox _search = new();
        private readonly Dictionary<string, Button> _facilityTabButtons = new(StringComparer.Ordinal);
        private static readonly string[] FacilityUiOrder =
        {
            "목재 가공 시설", "금속 가공 시설", "가죽 가공 시설",
            "옷감 가공 시설", "약품 가공 시설", "식재료 가공 시설"
        };
        private readonly Label _condition = new(), _materials = new(), _progressText = new(), _elapsed = new(),
            _state = new(), _cli = new(), _character = new(), _collection = new();
        private readonly Label _selectedName = new(), _percentage = new();
        private readonly Panel _itemIcon = new(), _toolIcon = new();
        private readonly Panel _progress = new();
        private int _percent;
        private float _layoutScale = 1;
        private readonly Button _start, _stop, _reload;
        private readonly ListBox _alteringQueue = new();
        private Button? _addQueue, _removeQueue, _clearQueue;
        private object[] _choices = Array.Empty<object>();
        private bool _loading, _loaded;
        internal string CliStatus = "확인 전", CharacterStatus = "확인 전";
        internal int? CompletedWorks;
        internal IReadOnlyList<AlteringPlan> QueuedAlteringPlans
            => _alteringQueue.Items.Cast<AlteringQueueChoice>().Select(x => x.Plan).ToArray();
        internal string? SelectedName => Items.SelectedItem switch
        { GatheringChoice g => g.Item.DisplayName, RecipeChoice r => r.Recipe.DisplayName, _ => null };
        internal string? OutputName => Items.SelectedItem is RecipeChoice r
            ? new AlteringPlan(AlteringPlan.Facilities[0], r.Recipe.DisplayName, 1, r.Recipe.ProducedPerWork, false).OutputName : SelectedName;

        internal ProductionPage(MainForm owner, bool altering)
        {
            _owner = owner; IsAltering = altering;
            Quantity = new QuantityTextBox
            {
                Minimum = 1,
                Maximum = 1000000,
                Value = 100
            };
            Name = altering ? "AlteringPage" : "GatheringPage";
            AccessibleName = altering ? "자동 가공" : "자동 채집";
            BackColor = WindowBg; ForeColor = TitleText; Visible = false;
            Font = new Font("맑은 고딕", 12f);
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                Padding = new Padding(14, 6, 14, 10), Margin = Padding.Empty, BackColor = WindowBg
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 58)); header.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
            header.Controls.Add(owner.SectionTitle(AccessibleName, 26), 0, 0);
            header.Controls.Add(new Label
            {
                Text = altering ? "품목과 목표 수량을 작업 목록에 담으면 다중가공으로 순서대로 실행합니다." : "분류 탭 없이 검색으로 원하는 채집 재료를 빠르게 찾습니다.",
                Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft
            }, 0, 1);
            root.Controls.Add(header, 0, 0);

            Items.AccessibleName = altering ? "가공 제법" : "채집 품목";
            StyleListBox(Items);
            if (altering)
            {
                _alteringQueue.AccessibleName = "다중가공 작업 목록";
                StyleListBox(_alteringQueue);
            }
            Quantity.AccessibleName = AccessibleName + " 목표 수량"; StyleField(Quantity);
            Facility.DropDownStyle = ComboBoxStyle.DropDownList;
            Facility.Items.AddRange(FacilityUiOrder); Facility.SelectedIndex = 0;
            _search.PlaceholderText = "품목을 검색하세요"; StyleField(_search);

            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0, 0, 0, 10), BackColor = Color.Transparent, AccessibleName = "생산 2열 본문"
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58)); body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

            var leftCard = new LauncherCard
            {
                Padding = new Padding(16, 10, 16, 10), Margin = new Padding(0, 0, 9, 0),
                AccessibleName = altering ? "가공 품목 영역" : "채집 품목 영역"
            };
            int leftRows = altering ? 6 : 4;
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = leftRows, Margin = Padding.Empty, BackColor = Color.Transparent };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            var settingsHeading = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = Padding.Empty };
            settingsHeading.Controls.Add(new Label { Text = altering ? "가공 설정" : "채집 설정", Dock = DockStyle.Fill, ForeColor = TitleText, TextAlign = ContentAlignment.MiddleLeft });
            _reload = PageButton("목록 새로고침", () => _ = LoadCatalogAsync(true));
            _reload.AccessibleName = AccessibleName + " 목록 새로고침"; _reload.Dock = DockStyle.Right; _reload.Width = 110; _reload.Margin = new Padding(2);
            settingsHeading.Controls.Add(_reload);
            if (!altering)
            {
                _inspect = PageButton("CLI 검사", () => _ = owner.InspectGatheringCliAsync());
                _inspect.AccessibleName = "CLI 검사"; _inspect.Dock = DockStyle.Right; _inspect.Width = 90; _inspect.Margin = new Padding(2);
                settingsHeading.Controls.Add(_inspect);
            }
            left.Controls.Add(settingsHeading, 0, 0);
            int row = 1;
            if (altering)
            {
                left.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
                left.Controls.Add(BuildFacilityTabs(), 0, row++);
            }
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
            left.Controls.Add(BuildLabeledField("품목 검색", _search, "production-field"), 0, row++);
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.Controls.Add(BuildListArea(altering ? "가공 품목" : "채집 품목"), 0, row++);
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            left.Controls.Add(BuildLabeledField("목표 수량", Quantity, "production-field"), 0, row++);
            if (altering)
            {
                left.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
                left.Controls.Add(BuildAlteringQueueArea(), 0, row++);
            }
            leftCard.Controls.Add(left); body.Controls.Add(leftCard, 0, 0);

            var right = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 1, Margin = Padding.Empty,
                BackColor = Color.Transparent, AccessibleName = "실행 상태"
            };
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var statusCard = new LauncherCard { Padding = new Padding(14, 10, 14, 10), Margin = Padding.Empty };
            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Margin = Padding.Empty, BackColor = Color.Transparent };
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 34)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 14)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); status.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            status.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            status.Controls.Add(owner.SectionTitle("실행 상태", 15), 0, 0);
            var progressLine = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Color.Transparent };
            progressLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); progressLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            progressLine.Controls.Add(_progressText, 0, 0); progressLine.Controls.Add(_percentage, 1, 0);
            foreach (var label in new[] { _state, _condition, _progressText, _percentage, _elapsed, _collection, _character, _cli })
            {
                label.Dock = DockStyle.Fill; label.ForeColor = TitleText; label.TextAlign = ContentAlignment.MiddleLeft;
                label.AutoEllipsis = true; label.BackColor = Color.Transparent; label.Margin = Padding.Empty;
            }
            _percentage.TextAlign = ContentAlignment.MiddleRight;
            _progressText.ForeColor = _percentage.ForeColor = Color.FromArgb(36, 195, 255);
            _progress.Dock = DockStyle.Fill; _progress.Margin = new Padding(0, 3, 0, 3); _progress.Paint += (_, e) => DrawProgress(e.Graphics);
            status.Controls.Add(_state, 0, 1); status.Controls.Add(_condition, 0, 2); status.Controls.Add(progressLine, 0, 3);
            status.Controls.Add(_progress, 0, 4); status.Controls.Add(_elapsed, 0, 5); status.Controls.Add(_collection, 0, 6);
            status.Controls.Add(_character, 0, 7); status.Controls.Add(_cli, 0, 8);
            statusCard.Controls.Add(status); right.Controls.Add(statusCard, 0, 0);
            body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 1);

            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 4, 0, 0), BackColor = Color.Transparent, AccessibleName = "하단 시작 정지" };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            _start = PageButton("▶   " + AccessibleName + " 시작 (F9)", owner.StartSelected); _start.BackColor = Accent; _start.AccessibleName = AccessibleName + " 시작";
            _stop = PageButton("■   정지 (F10)", owner.StopSelected); _stop.AccessibleName = "정지";
            actions.Controls.Add(_start, 0, 0); actions.Controls.Add(_stop, 1, 0); root.Controls.Add(actions, 0, 2);
            Controls.Add(root);

            foreach (var control in Descendants(root).Prepend(root)) control.Font = new Font("맑은 고딕", 18f, control.Font.Style, GraphicsUnit.Pixel);
            void Typography(Control control, float pixels) => control.Font = new Font("맑은 고딕", pixels, control.Font.Style, GraphicsUnit.Pixel);
            Typography(header.Controls[0], 36); Typography(header.Controls[1], 18); Typography(settingsHeading.Controls[0], 21);
            Typography(status.Controls[0], 21); Typography(_reload, 14);
            if (_inspect is not null) Typography(_inspect, 14);
            if (_addQueue is not null) Typography(_addQueue, 13);
            if (_removeQueue is not null) Typography(_removeQueue, 13);
            if (_clearQueue is not null) Typography(_clearQueue, 13);
            foreach (var tab in _facilityTabButtons.Values) Typography(tab, 17);
            foreach (var label in new[] { _state, _condition, _elapsed, _collection, _character, _cli }) Typography(label, 17);
            Typography(_progressText, 18); Typography(_percentage, 18); Typography(_start, 22); Typography(_stop, 22);

            var metrics = Descendants(root).Prepend(root).Select(c => new PageMetric(c, c.Font.FontFamily.Name, c.Font.Size, c.Font.Style, c.Padding, c.Margin)).ToArray();
            var rows = metrics.SelectMany(m => m.Control is TableLayoutPanel t ? t.RowStyles.Cast<RowStyle>().Where(x => x.SizeType == SizeType.Absolute).Select(x => (Style: x, Size: x.Height)) : Enumerable.Empty<(RowStyle Style, float Size)>()).ToArray();
            var columns = metrics.SelectMany(m => m.Control is TableLayoutPanel t ? t.ColumnStyles.Cast<ColumnStyle>().Where(x => x.SizeType == SizeType.Absolute).Select(x => (Style: x, Size: x.Width)) : Enumerable.Empty<(ColumnStyle Style, float Size)>()).ToArray();
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
                _reload.Width = Math.Max(70, (int)(110 * scale)); if (_inspect is not null) _inspect.Width = Math.Max(64, (int)(90 * scale));
                Items.ItemHeight = Math.Max(26, Items.Font.Height + Math.Max(7, (int)(8 * scale)));
                foreach (var field in Descendants(root).Where(c => c.Tag is string tag && tag == "production-field")) LayoutField(field);
                root.ResumeLayout(true); fitting = false;
            }
            Resize += (_, _) => FitPage(); DpiChangedAfterParent += (_, _) => FitPage(); FitPage();
            _search.TextChanged += (_, _) => Filter(); Items.SelectedIndexChanged += (_, _) => SelectionChanged(); Quantity.ValueChanged += (_, _) => SelectionChanged();
            Facility.SelectedIndexChanged += (_, _) => { UpdateFacilityTabs(); Filter(); };
            Owned.Text = "—"; UpdateExecution();
        }

        private TableLayoutPanel BuildFacilityTabs()
        {
            var tabs = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = FacilityUiOrder.Length,
                RowCount = 1,
                Margin = new Padding(0, 4, 0, 4),
                BackColor = Color.Transparent,
                AccessibleName = "가공 시설 탭"
            };
            tabs.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            foreach (string facility in FacilityUiOrder)
            {
                tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / FacilityUiOrder.Length));
                string title = facility switch
                {
                    "목재 가공 시설" => "목재",
                    "금속 가공 시설" => "금속",
                    "가죽 가공 시설" => "가죽",
                    "옷감 가공 시설" => "옷감",
                    "약품 가공 시설" => "약품",
                    "식재료 가공 시설" => "식재료",
                    _ => facility.Replace(" 시설", "")
                };
                var button = PageButton(title, () =>
                {
                    if (Facility.Items.Contains(facility))
                        Facility.SelectedItem = facility;
                });
                button.AccessibleName = "가공 시설 " + title;
                button.Margin = new Padding(2);
                button.Font = new Font("맑은 고딕", 10.5f, FontStyle.Bold);
                _facilityTabButtons[facility] = button;
                tabs.Controls.Add(button);
            }

            UpdateFacilityTabs();
            return tabs;
        }

        private void UpdateFacilityTabs()
        {
            string? selected = Facility.SelectedItem?.ToString();
            foreach (var pair in _facilityTabButtons)
            {
                pair.Value.BackColor = pair.Key == selected ? Accent : CardBg2;
                pair.Value.ForeColor = pair.Key == selected ? Color.White : TitleText;
                pair.Value.Invalidate();
            }
        }

        private static string? RecipeFacility(AlteringRecipe recipe)
            => AlteringFacilityResolver.Resolve(recipe);

        private static void StyleField(Control control)
        {
            control.Dock = DockStyle.Fill; control.BackColor = CardBg2; control.ForeColor = TitleText;
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
            var surface = new LauncherCard { Dock = DockStyle.None, BackColor = CardBg2, Margin = Padding.Empty };
            control.Dock = DockStyle.None; surface.Controls.Add(control); field.Controls.Add(surface);
            field.Resize += (_, _) => LayoutField(field); layout.Controls.Add(field, 0, 1); LayoutField(field); return layout;
        }

        private Control BuildListArea(string caption)
        {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, BackColor = Color.Transparent };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = TitleText, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("맑은 고딕", 17f, FontStyle.Bold, GraphicsUnit.Pixel) }, 0, 0);
            var surface = new LauncherCard { Dock = DockStyle.Fill, BackColor = CardBg2, Padding = new Padding(5), Margin = new Padding(0, 2, 0, 2) };
            surface.Controls.Add(Items); layout.Controls.Add(surface, 0, 1); return layout;
        }

        private Control BuildAlteringQueueArea()
        {
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Margin = Padding.Empty,
                BackColor = Color.Transparent,
                AccessibleName = "다중가공 작업 목록 영역"
            };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var header = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = Padding.Empty
            };
            header.Controls.Add(new Label
            {
                Text = "다중가공 작업 목록",
                Dock = DockStyle.Fill,
                ForeColor = TitleText,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("맑은 고딕", 16f, FontStyle.Bold, GraphicsUnit.Pixel)
            });

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Right,
                Width = 230,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                BackColor = Color.Transparent
            };
            _addQueue = PageButton("+ 추가", AddCurrentAlteringQueue);
            _removeQueue = PageButton("삭제", RemoveSelectedAlteringQueue);
            _clearQueue = PageButton("비우기", ClearQueuedAlteringPlans);
            foreach (var button in new[] { _addQueue, _removeQueue, _clearQueue })
            {
                button.Dock = DockStyle.None;
                button.Width = 72;
                button.Height = 29;
                button.Margin = new Padding(2, 1, 2, 1);
            }
            buttons.Controls.Add(_addQueue);
            buttons.Controls.Add(_removeQueue);
            buttons.Controls.Add(_clearQueue);
            header.Controls.Add(buttons);
            layout.Controls.Add(header, 0, 0);

            var surface = new LauncherCard
            {
                Dock = DockStyle.Fill,
                BackColor = CardBg2,
                Padding = new Padding(5),
                Margin = new Padding(0, 2, 0, 2)
            };
            surface.Controls.Add(_alteringQueue);
            layout.Controls.Add(surface, 0, 1);
            return layout;
        }

        private void AddCurrentAlteringQueue()
        {
            if (!IsAltering || _owner.AnyRunning || _loading)
                return;

            try
            {
                var plan = _owner.SelectedAlteringPlan();
                var queued = _alteringQueue.Items.Cast<AlteringQueueChoice>().ToArray();
                int existing = Array.FindIndex(queued, x =>
                    x.Plan.FacilityName == plan.FacilityName &&
                    x.Plan.DisplayName == plan.DisplayName &&
                    x.Plan.RecipeOrdinal == plan.RecipeOrdinal);

                var item = new AlteringQueueChoice(plan);
                if (existing >= 0)
                {
                    _alteringQueue.Items[existing] = item;
                    _alteringQueue.SelectedIndex = existing;
                    _owner._log.Write(
                        $"[다중가공] 작업 목표 갱신 · {plan.DisplayName} {plan.TargetQuantity:N0}개");
                }
                else
                {
                    _alteringQueue.Items.Add(item);
                    _alteringQueue.SelectedIndex = _alteringQueue.Items.Count - 1;
                    _owner._log.Write(
                        $"[다중가공] 작업 추가 · {plan.ScreenTitle} · {plan.DisplayName} {plan.TargetQuantity:N0}개");
                }

                UpdateExecution();
            }
            catch (Exception ex)
            {
                _owner._log.Write("[다중가공] 작업 추가 실패: " + ex.Message);
            }
        }

        private void RemoveSelectedAlteringQueue()
        {
            if (_owner.AnyRunning || _alteringQueue.SelectedIndex < 0)
                return;
            string removed = _alteringQueue.SelectedItem?.ToString() ?? "작업";
            _alteringQueue.Items.RemoveAt(_alteringQueue.SelectedIndex);
            _owner._log.Write("[다중가공] 작업 삭제 · " + removed);
            UpdateExecution();
        }

        internal void ClearQueuedAlteringPlans()
        {
            if (_owner.AnyRunning)
                return;
            if (_alteringQueue.Items.Count == 0)
                return;
            _alteringQueue.Items.Clear();
            _owner._log.Write("[다중가공] 작업 목록 비움");
            UpdateExecution();
        }

        private Control BuildSummaryCard(string caption, bool condition)
        {
            var card = new LauncherCard { Padding = new Padding(12, 6, 12, 7), Margin = new Padding(0, condition ? 4 : 0, 0, condition ? 0 : 4) };
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, BackColor = Color.Transparent };
            layout.ColumnStyles.Add(new(SizeType.Absolute, 0)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
            layout.RowStyles.Add(new(SizeType.Absolute, 24)); layout.RowStyles.Add(new(SizeType.Percent, 50)); layout.RowStyles.Add(new(SizeType.Percent, 50));
            var heading = new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = TitleText, Font = new Font("맑은 고딕", 13f, FontStyle.Bold), BackColor = Color.Transparent };
            layout.Controls.Add(heading, 0, 0); layout.SetColumnSpan(heading, 2);
            var icon = condition ? _toolIcon : _itemIcon;
            icon.Dock = DockStyle.Fill; icon.BackColor = Color.Transparent; icon.Margin = new Padding(0, 1, 6, 0);
            icon.Paint += (_, e) => DrawResourceIcon(e.Graphics, icon.ClientRectangle, condition);
            // Keep the summary text-only; no material or tool artwork.
            layout.Controls.Add(condition ? _condition : _selectedName, 1, 1);
            layout.Controls.Add(condition ? _materials : Owned, 1, 2);
            card.Controls.Add(layout); return card;
        }

        private static GraphicsPath Rounded(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath(); float d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90); path.AddArc(bounds.Right-d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right-d, bounds.Bottom-d, d, d, 0, 90); path.AddArc(bounds.X, bounds.Bottom-d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }

        private sealed class LauncherCard : Panel
        {
            internal LauncherCard()
            {
                Dock = DockStyle.Fill; BackColor = CardBg;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width < 3 || Height < 3) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(SurfaceColor(Parent));
                var bounds = new RectangleF(1, 1, Width-2, Height-2);
                using var shape = Rounded(bounds, 12);
                using var fill = new LinearGradientBrush(bounds, Color.FromArgb(7, 35, 61), CardBg2, 60f);
                using var border = new Pen(Color.FromArgb(21, 77, 116));
                e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
            }
        }

        private void DrawProgress(Graphics graphics)
        {
            if (_progress.Width < 2 || _progress.Height < 2) return;
            graphics.Clear(CardBg); graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0, 0, _progress.Width-1, _progress.Height-1);
            using var path = Rounded(bounds, 7); using var track = new SolidBrush(Line);
            graphics.FillPath(track, path);
            if (_percent > 0)
            {
                using var filled = Rounded(new RectangleF(0, 0, Math.Max(8, bounds.Width*_percent/100f), bounds.Height), 7);
                using var fill = new LinearGradientBrush(bounds, Color.FromArgb(25, 209, 255), Accent, 90f);
                graphics.FillPath(fill, filled);
            }
        }

        private void DrawResourceIcon(Graphics graphics, Rectangle bounds, bool tool)
        {
            if (bounds.Width < 2 || bounds.Height < 2) return;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var saved = graphics.Save();
            float size = Math.Min(bounds.Width, bounds.Height);
            graphics.TranslateTransform((bounds.Width-size)/2, (bounds.Height-size)/2); graphics.ScaleTransform(size/64, size/64);
            if (tool)
            {
                // Decorative tool symbol, not a claim about the exact equipped tool.
                using var handle = new Pen(Color.FromArgb(153, 102, 60), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawLine(handle, 14, 54, 45, 16);
                using var steel = new Pen(Color.FromArgb(190, 216, 242), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                graphics.DrawLine(steel, 21, 14, 49, 29);
            }
            else
            {
                // Neutral material artwork: CLI selection text remains authoritative.
                PointF[] top = IsAltering ? new[] {new PointF(8,28),new PointF(39,10),new PointF(58,22),new PointF(27,40)}
                    : new[] {new PointF(12,24),new PointF(27,10),new PointF(47,18),new PointF(54,40),new PointF(29,50),new PointF(8,40)};
                using var ore = new LinearGradientBrush(new Rectangle(5,5,55,50), Color.FromArgb(226, 233, 245), Color.FromArgb(110, 133, 165), 50f);
                graphics.FillPolygon(ore, top);
                using var facet = new SolidBrush(Color.FromArgb(91, 116, 148));
                graphics.FillPolygon(facet, IsAltering ? new[] {new PointF(27,40),new PointF(58,22),new PointF(55,38),new PointF(25,56)}
                    : new[] {new PointF(27,28),new PointF(47,18),new PointF(54,40),new PointF(29,50)});
                using var edge = new Pen(Color.FromArgb(220, 234, 250), 1);
                graphics.DrawPolygon(edge, top);
            }
            graphics.Restore(saved);
        }
        private sealed class LauncherCombo : ComboBox
        {
            internal LauncherCombo() { SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(CardBg2);
                TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(4, 0, Math.Max(1, Width - 32), Height), Enabled ? TitleText : Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(e.Graphics, "⌄", Font, new Rectangle(Width - 28, 0, 24, Height), Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -2, -2));
            }
            protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
        }
        private static void StyleCombo(ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.DrawItem += (_, e) =>
            {
                using var background = new SolidBrush((e.State & DrawItemState.Selected) != 0 ? AccentSoft : CardBg2);
                e.Graphics.FillRectangle(background, e.Bounds);
                string text = e.Index >= 0 ? combo.Items[e.Index]?.ToString() ?? "" : "품목을 선택하세요";
                TextRenderer.DrawText(e.Graphics, text, combo.Font, Rectangle.Inflate(e.Bounds, -5, 0),
                    combo.Enabled ? TitleText : Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }
        private static Button PageButton(string caption, Action action)
        {
            var button = new ProductionButton { Text = caption, Dock = DockStyle.Fill, BackColor = CardBg2, ForeColor = TitleText,
                Font = new Font("맑은 고딕", 15f, FontStyle.Bold), Margin = new Padding(3), Cursor = Cursors.Hand };
            button.Click += (_, _) => action(); return button;
        }
        private sealed class ProductionButton : Button
        {
            internal ProductionButton() { SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }
            protected override void OnPaint(PaintEventArgs e)
            {
                if (Width < 3 || Height < 3) return;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.Clear(SurfaceColor(Parent));
                var bounds = new RectangleF(1, 1, Width-2, Height-2);
                using var shape = Rounded(bounds, 10);
                bool primary = Enabled && BackColor == Accent;
                using var fill = new LinearGradientBrush(bounds, primary ? Color.FromArgb(4, 165, 255) : CardBg,
                    primary ? Color.FromArgb(0, 86, 226) : CardBg2, 90f);
                using var border = new Pen(primary ? Color.FromArgb(0, 207, 255) : Line, primary ? 1.8f : 1f);
                e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(border, shape);
                TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, Enabled ? ForeColor : Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
            }
        }
        private void AddRow(TableLayoutPanel form, int row, string caption, Control control)
        {
            form.Controls.Add(new Label { Text = caption, Dock = DockStyle.Fill, ForeColor = Muted, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var field = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(3, 5, 3, 5), Tag = "production-field" };
            var surface = new LauncherCard { Dock = DockStyle.None, BackColor = CardBg2 };
            control.Dock = DockStyle.None;
            surface.Controls.Add(control); field.Controls.Add(surface);
            field.Resize += (_, _) => LayoutField(field);
            form.Controls.Add(field, 1, row); LayoutField(field);
        }

        private void LayoutField(Control field)
        {
            var surface = field.Controls[0]; var control = surface.Controls[0];
            int height = Math.Min(field.Height, Math.Max(control.Font.Height + 8, (int)(44 * _layoutScale)));
            int inset = Math.Max(4, (int)(11 * _layoutScale));
            surface.Bounds = new Rectangle(0, Math.Max(0, (field.Height - height) / 2), field.Width, height);
            control.Width = Math.Max(1, surface.Width - inset * 2);
            control.Location = new Point(inset, Math.Max(0, (surface.Height - control.Height) / 2));
        }

        private sealed record PageMetric(Control Control, string Family, float Size, FontStyle Style, Padding Padding, Padding Margin);
        private static Padding ScalePadding(Padding p, float scale) => new((int)(p.Left * scale), (int)(p.Top * scale), (int)(p.Right * scale), (int)(p.Bottom * scale));
        private static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls) { yield return child; foreach (var nested in Descendants(child)) yield return nested; }
        }

        private static Color SurfaceColor(Control? control)
        {
            while (control != null)
            {
                if (control.BackColor.A == 255) return control.BackColor;
                control = control.Parent;
            }
            return WindowBg;
        }

        internal async Task LoadCatalogAsync(bool force = false)
        {
            if (_loading || (_loaded && !force) || _owner.AnyRunning || _owner._gatheringDiagnosticRunning) return;
            _loading = true; UpdateExecution();
            try
            {
                if (IsAltering)
                {
                    _choices = (await new AlteringCliData(_owner._cli).RecipesAsync(CancellationToken.None))
                        .GroupBy(x => (Facility: RecipeFacility(x), x.DisplayName))
                        .Select(g => (object)new RecipeChoice(g.First())).ToArray();
                }
                else
                {
                    string? selectedBeforeRefresh = SelectedName;
                    var raw = await _owner._cli.GetGatherableItemsAsync(CancellationToken.None);
                    if (!raw.Success)
                        throw new InvalidDataException("채집 목록 원본 조회에 실패했습니다.");

                    _owner._log.Write("[자동 채집][CLI 구조] " +
                        GatheringQueries.DescribeCatalogSchema(raw, selectedBeforeRefresh));

                    _choices = GatheringQueries.ParseCatalog(raw)
                        .Select(x => (object)new GatheringChoice(x)).ToArray();
                }
                _loaded = true;
                if (IsDisposed) return;
                if (IsAltering) ApplySavedAlteringSessionSelection();
                else Filter();
                CliStatus = "정상";
                await _owner.RefreshProductionStateAsync(this);
            }
            catch (Exception ex)
            {
                if (IsDisposed) return;
                _loaded = false; _choices = Array.Empty<object>(); Items.Items.Clear();
                _condition.Text = "목록 조회 실패 · 새로고침하세요"; Owned.Text = "조회 실패";
                CliStatus = "연결 확인 필요";
                _owner._log.Write($"[{AccessibleName}] 목록 조회 실패: {ex.Message}");
            }
            finally { _loading = false; if (!IsDisposed) UpdateExecution(); }
        }

        private void ApplySavedAlteringSessionSelection()
        {
            if (!IsAltering) return;
            try
            {
                var saved = new AlteringSessionStore().Load();
                if (saved is null)
                {
                    Filter();
                    return;
                }

                if (Facility.Items.Contains(saved.FacilityName))
                    Facility.SelectedItem = saved.FacilityName;
                Filter();

                int recipeIndex = Items.Items.Cast<object>().ToList().FindIndex(x =>
                    x is RecipeChoice r && r.Recipe.DisplayName == saved.DisplayName);
                if (recipeIndex < 0) return;

                Items.SelectedIndex = recipeIndex;
                Quantity.Value = Math.Clamp(saved.TargetQuantity, Quantity.Minimum, Quantity.Maximum);
                _owner._productionProgressSummary =
                    $"이어하기 대기 · {saved.DisplayName} · 등록 {saved.QueuedWorks}/{saved.RequiredWorks} · 마지막 단계 {saved.Stage}";
                _owner._log.Write(
                    $"[자동 가공] 이어하기 기록 발견 · {saved.DisplayName} {saved.TargetQuantity}개 · 등록 {saved.QueuedWorks}/{saved.RequiredWorks} · 단계={saved.Stage}");
            }
            catch (Exception ex)
            {
                _owner._log.Write("[자동 가공] 이어하기 기록 확인 실패: " + ex.Message);
            }
        }

        private void Filter()
        {
            string? previous = SelectedName;
            IEnumerable<object> visible = _choices;

            if (IsAltering && Facility.SelectedItem is string facility)
                visible = visible.Where(x => x is RecipeChoice r &&
                    RecipeFacility(r.Recipe) == facility);
            string search = _search.Text.Trim();
            if (!string.IsNullOrWhiteSpace(search))
                visible = visible.Where(x => x.ToString()!.Contains(search, StringComparison.OrdinalIgnoreCase));

            Items.BeginUpdate();
            Items.Items.Clear();
            Items.Items.AddRange(visible.ToArray());
            int index = Items.Items.Cast<object>().ToList().FindIndex(x => x.ToString() == previous);
            if (Items.Items.Count > 0) Items.SelectedIndex = Math.Max(0, index);
            Items.EndUpdate();
            SelectionChanged();
        }

        private void SelectionChanged()
        {
            Owned.Text = "확인 중"; _owner._productionNextPoll = DateTime.MinValue;
            _selectedName.Text = SelectedName ?? "품목을 선택하세요";
            _itemIcon.Invalidate();
            if (Items.SelectedItem is GatheringChoice g)
            { _condition.Text = g.Item.ToolOk ? "사용 가능" : "확인 필요"; _condition.ForeColor = g.Item.ToolOk ? Green : Color.Orange; _materials.Text = g.Item.ToolOk ? "선택 품목의 도구 사용 가능" : "채집 도구를 확인하세요"; }
            else if (Items.SelectedItem is RecipeChoice r)
            {
                _condition.Text = r.Recipe.Alterable ? "가능" : r.Recipe.MissingIngredients.Count > 0 ? "재료 부족" : "확인 필요";
                _condition.ForeColor = r.Recipe.Alterable ? Green : Color.Orange;
                string facility = Facility.SelectedItem?.ToString() ?? FacilityUiOrder[0];
                var previewPlan = new AlteringPlan(
                    facility, r.Recipe.DisplayName, (int)Quantity.Value, r.Recipe.ProducedPerWork, false);
                _materials.Text = AlteringMaterialEstimate.Describe(previewPlan, r.Recipe) +
                    $" · 1회 {r.Recipe.ProducedPerWork}개";
            }
            else { _condition.Text = "품목을 선택하세요"; _materials.Text = "—"; Owned.Text = "—"; }
            UpdateExecution();
        }

        internal void UpdateExecution()
        {
            bool running = _owner.AnyRunning && !_owner._gatheringDiagnosticRunning;
            bool busy = running || _owner._starting || _owner._gatheringDiagnosticRunning;
            bool available = Items.SelectedItem switch
            {
                GatheringChoice { Item.ToolOk: true } => true,
                RecipeChoice r => r.Recipe.Alterable || r.Recipe.MissingIngredients.Count > 0,
                _ => false
            };
            foreach (var field in new Control[] { Items, Quantity, _reload }) field.Enabled = !busy && !_loading;
            _search.Enabled = !busy && !_loading;
            Facility.Enabled = !busy && !_loading;
            foreach (var tab in _facilityTabButtons.Values) tab.Enabled = !busy && !_loading;
            if (_inspect is not null) _inspect.Enabled = !busy && !_loading && SelectedName is not null;
            if (_addQueue is not null) _addQueue.Enabled = !busy && !_loading && available;
            if (_removeQueue is not null) _removeQueue.Enabled = !busy && _alteringQueue.SelectedIndex >= 0;
            if (_clearQueue is not null) _clearQueue.Enabled = !busy && _alteringQueue.Items.Count > 0;
            _alteringQueue.Enabled = !busy;
            bool hasBatch = IsAltering && _alteringQueue.Items.Count > 0;
            _start.Text = hasBatch
                ? $"▶   다중가공 {_alteringQueue.Items.Count}종 시작 (F9)"
                : "▶   " + AccessibleName + " 시작 (F9)";
            _start.Enabled = !busy && !_loading && (available || hasBatch); _stop.Enabled = running;
            bool ownRun = _owner._activeMode == (IsAltering ? "가공" : "채집");
            bool ownResult = _owner._productionLastMode == (IsAltering ? "가공" : "채집") && _owner._productionDisplayName == SelectedName;
            long current = ownRun || ownResult ? _owner._productionCurrentQuantity : 0;
            int target = ownRun ? _owner._productionTargetQuantity : (int)Quantity.Value;
            int percent = target > 0 ? (int)Math.Clamp(current * 100 / target, 0, 100) : 0;
            _progressText.Text = $"진행 수량  {current:N0} / {target:N0}";
            _percentage.Text = $"진행률  {percent}%"; _percent = percent; _progress.Invalidate();
            _progressText.AccessibleName = $"진행 수량 {current} / {target}, 진행률 {percent}%";
            var startedAt = _owner._dungeonStartedAt; var stoppedAt = _owner._dungeonStoppedAt;
            var elapsed = ownRun && startedAt.HasValue ? DateTime.Now - startedAt.Value
                : ownResult && stoppedAt.HasValue && startedAt.HasValue ? stoppedAt.Value - startedAt.Value : TimeSpan.Zero;
            string state = ownRun ? "실행 중" : _loading ? "목록 조회 중" : ownResult && _owner._runError is not null ? "오류" : "대기 중";
            _state.Text = $"현재 상태  {state}";
            _condition.Text = $"현재 단계  {(ownRun ? _owner._statusValue.Text : "—")}"; _condition.ForeColor = TitleText;
            _elapsed.Text = $"경과 시간  {elapsed:hh\\:mm\\:ss}";
            _collection.Text = IsAltering ? $"완료 작업  {CompletedWorks?.ToString() ?? "0"}회   ·   획득 수량  {current:N0}개" : $"획득 수량  {current:N0}개";
            _character.Text = $"오류 횟수  {(_owner._runError is null ? 0 : 1)}회";
            _cli.Text = $"CLI 상태  {CliStatus}"; _cli.ForeColor = CliStatus == "정상" ? Green : Muted;
            _character.ForeColor = _owner._runError is null ? Muted : Color.Salmon;
        }
    }
    private string? _productionLastMode;

    // Observe the core's post-existing-work baseline without changing its workflow.
    private sealed class ProductionAlteringData(IAlteringData inner, Action<long> baseline) : IAlteringData
    {
        private bool _captured;
        private long _baseline;
        internal long Gained { get; private set; }
        public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct) => inner.RecipesAsync(ct);
        public Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct) => inner.WorksAsync(ct);
        public async Task<long> ItemCountAsync(string name, CancellationToken ct)
        {
            long value = await inner.ItemCountAsync(name, ct);
            if (!_captured) { _captured = true; _baseline = value; baseline(value); }
            Gained = Math.Max(0, value - _baseline);
            return value;
        }
    }
}
