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
        if (_productionPolling) return;
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

    // Native controls in the existing central content region, sharing its palette.
    // Independent pages retain their own selections and never appear together.
    private sealed class ProductionPage : Panel
    {
        private readonly MainForm _owner;
        internal bool IsAltering { get; }
        internal readonly ComboBox Items = new LauncherCombo(), Facility = new LauncherCombo();
        internal readonly NumericUpDown Quantity = new ArrowlessNumericUpDown { Minimum = 1, Maximum = 1000000, Value = 100 };
        internal readonly Label Owned = new();
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
        private object[] _choices = Array.Empty<object>();
        private bool _loading, _loaded;
        internal string CliStatus = "확인 전", CharacterStatus = "확인 전";
        internal int? CompletedWorks;
        internal string? SelectedName => Items.SelectedItem switch
        { GatheringChoice g => g.Item.DisplayName, RecipeChoice r => r.Recipe.DisplayName, _ => null };
        internal string? OutputName => Items.SelectedItem is RecipeChoice r
            ? new AlteringPlan(AlteringPlan.Facilities[0], r.Recipe.DisplayName, 1, r.Recipe.ProducedPerWork, false).OutputName : SelectedName;

        internal ProductionPage(MainForm owner, bool altering)
        {
            _owner = owner; IsAltering = altering;
            Name = altering ? "AlteringPage" : "GatheringPage";
            AccessibleName = altering ? "자동 가공" : "자동 채집";
            BackColor = WindowBg; ForeColor = TitleText; Visible = false;
            Font = new Font("맑은 고딕", 12f);
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 6, 14, 10), Margin = Padding.Empty };
            root.RowStyles.Add(new(SizeType.Absolute, 82));
            root.RowStyles.Add(new(SizeType.Percent, 51)); root.RowStyles.Add(new(SizeType.Percent, 49));
            var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
            header.RowStyles.Add(new(SizeType.Percent, 58)); header.RowStyles.Add(new(SizeType.Percent, 42));
            header.Controls.Add(owner.SectionTitle(AccessibleName, 26));
            header.Controls.Add(new Label { Text = altering ? "보유 재료로 원하는 아이템을 자동 가공합니다." : "채집 재료를 자동으로 수집합니다.", Dock = DockStyle.Fill, ForeColor = Muted });
            root.Controls.Add(header, 0, 0);

            var settings = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 0, 0, 10) };
            settings.ColumnStyles.Add(new(SizeType.Percent, 62)); settings.ColumnStyles.Add(new(SizeType.Percent, 38));
            var card = new LauncherCard { Padding = new Padding(16, 9, 16, 9), Margin = new Padding(0, 0, 9, 0) };
            var form = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = altering ? 4 : 3, BackColor = Color.Transparent };
            form.ColumnStyles.Add(new(SizeType.Absolute, 112)); form.ColumnStyles.Add(new(SizeType.Percent, 100));
            form.RowStyles.Add(new(SizeType.Absolute, 32));
            for (int i = 1; i < form.RowCount; i++) form.RowStyles.Add(new(SizeType.Percent, 100f / (form.RowCount - 1)));
            var settingsHeading = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
            settingsHeading.Controls.Add(new Label { Text = altering ? "가공 설정" : "채집 설정", Dock = DockStyle.Fill, ForeColor = TitleText, Font = new Font("맑은 고딕", 15f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft });
            form.Controls.Add(settingsHeading, 0, 0); form.SetColumnSpan(settingsHeading, 2);
            _reload = PageButton("목록 새로고침", () => _ = LoadCatalogAsync(true));
            _reload.Font = new Font("맑은 고딕", 8.5f); _reload.Dock = DockStyle.Right; _reload.Width = 110; settingsHeading.Controls.Add(_reload);

            Items.DropDownStyle = ComboBoxStyle.DropDownList; StyleField(Items); StyleCombo(Items);
            Items.AccessibleName = altering ? "가공 제법" : "채집 품목";
            Quantity.AccessibleName = AccessibleName + " 목표 수량"; StyleField(Quantity);

            Facility.DropDownStyle = ComboBoxStyle.DropDownList;
            Facility.Items.AddRange(FacilityUiOrder);
            Facility.SelectedIndex = 0;

            if (altering)
            {
                var facilityTabs = BuildFacilityTabs();
                form.Controls.Add(facilityTabs, 0, 1); form.SetColumnSpan(facilityTabs, 2);
                AddRow(form, 2, Items.AccessibleName, Items);
                AddRow(form, 3, "목표 수량", Quantity);
            }
            else
            {
                AddRow(form, 1, Items.AccessibleName, Items);
                AddRow(form, 2, "목표 수량", Quantity);
            }

            card.Controls.Add(form); settings.Controls.Add(card, 0, 0);
            var summaries = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            summaries.RowStyles.Add(new(SizeType.Percent, 52)); summaries.RowStyles.Add(new(SizeType.Percent, 48));
            summaries.Controls.Add(BuildSummaryCard(altering ? "선택한 가공 제법" : "선택한 채집 품목", false), 0, 0);
            summaries.Controls.Add(BuildSummaryCard(altering ? "가공 가능 여부" : "도구 상태", true), 0, 1);
            settings.Controls.Add(summaries, 1, 0); root.Controls.Add(settings, 0, 1);

            var execution = new LauncherCard { Padding = new Padding(16, 8, 16, 9), Margin = Padding.Empty };
            var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.Transparent };
            status.RowStyles.Add(new(SizeType.Absolute, 34)); status.RowStyles.Add(new(SizeType.Absolute, 59));
            status.RowStyles.Add(new(SizeType.Percent, 62)); status.RowStyles.Add(new(SizeType.Percent, 38)); status.RowStyles.Add(new(SizeType.Absolute, 23));
            status.Controls.Add(owner.SectionTitle("▶  실행 상태", 15), 0, 0);
            var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            actions.ColumnStyles.Add(new(SizeType.Percent, 72)); actions.ColumnStyles.Add(new(SizeType.Percent, 28));
            _start = PageButton("▶   " + AccessibleName + " 시작", owner.StartSelected); _start.BackColor = Accent;
            _start.AccessibleName = AccessibleName + " 시작";
            _stop = PageButton("■   정지 (F10)", owner.StopSelected); _stop.AccessibleName = "정지";
            actions.Controls.Add(_start, 0, 0); actions.Controls.Add(_stop, 1, 0); status.Controls.Add(actions, 0, 1);
            var progressCard = new LauncherCard { Padding = new Padding(12, 6, 12, 6), Margin = new Padding(2, 7, 2, 7) };
            var progressLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2, BackColor = Color.Transparent };
            progressLayout.ColumnStyles.Add(new(SizeType.Percent, 50)); progressLayout.ColumnStyles.Add(new(SizeType.Percent, 50));
            progressLayout.RowStyles.Add(new(SizeType.Absolute, 17)); progressLayout.RowStyles.Add(new(SizeType.Percent, 82)); progressLayout.RowStyles.Add(new(SizeType.Percent, 18));
            progressLayout.Controls.Add(new Label { Text = "진행 수량", ForeColor = Muted, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = new Font("맑은 고딕", 10f) }, 0, 0);
            progressLayout.Controls.Add(new Label { Text = "진행률", ForeColor = Muted, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = new Font("맑은 고딕", 10f) }, 1, 0);
            _progressText.Font = new Font("맑은 고딕", 22f, FontStyle.Bold); _progressText.ForeColor = Color.FromArgb(36, 195, 255);
            _percentage.Font = new Font("맑은 고딕", 22f, FontStyle.Bold); _percentage.ForeColor = _progressText.ForeColor;
            _percentage.Dock = DockStyle.Fill; _percentage.TextAlign = ContentAlignment.MiddleLeft;
            progressLayout.Controls.Add(_progressText, 0, 1); progressLayout.Controls.Add(_percentage, 1, 1);
            _progress.Dock = DockStyle.Fill; _progress.Margin = new Padding(1, 2, 1, 1);
            _progress.Paint += (_, e) => DrawProgress(e.Graphics);
            progressLayout.Controls.Add(_progress, 0, 2); progressLayout.SetColumnSpan(_progress, 2); progressCard.Controls.Add(progressLayout); status.Controls.Add(progressCard, 0, 2);
            var chips = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 4, Margin = Padding.Empty };
            chips.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (var label in new[] { _elapsed, _state, _cli, _character })
            {
                chips.ColumnStyles.Add(new(SizeType.Percent, 25));
                var chip = new LauncherCard { Padding = new Padding(9, 4, 6, 4), Margin = new Padding(2, 0, 2, 2) };
                chip.Controls.Add(label); chips.Controls.Add(chip);
            }
            status.Controls.Add(chips, 0, 3); status.Controls.Add(_collection, 0, 4);
            foreach (var label in new[] { Owned, _condition, _materials, _selectedName, _progressText, _elapsed, _state, _cli, _character, _collection })
            { label.Dock = DockStyle.Fill; label.TextAlign = ContentAlignment.MiddleLeft; label.AutoEllipsis = true; label.BackColor = Color.Transparent; }
            _selectedName.Font = new Font("맑은 고딕", 17f, FontStyle.Bold);
            _progressText.Margin = _percentage.Margin = Padding.Empty;
            Owned.Font = new Font("맑은 고딕", 13f, FontStyle.Bold); Owned.ForeColor = TitleText;
            _condition.Font = new Font("맑은 고딕", 17f, FontStyle.Bold);
            _materials.Font = new Font("맑은 고딕", 11f); _materials.ForeColor = Muted;
            _collection.Font = new Font("맑은 고딕", 8.5f); _collection.ForeColor = Muted;
            _elapsed.Font = _state.Font = _cli.Font = _character.Font = new Font("맑은 고딕", 10.5f);
            execution.Controls.Add(status); root.Controls.Add(execution, 0, 2); Controls.Add(root);
            // ReferenceDashboard uses pixel fonts at 1448x1086, with the same
            // minimum legible sizes below. Use its title/content hierarchy here.
            foreach (var control in Descendants(root).Prepend(root))
                control.Font = new Font("맑은 고딕", 18f, control.Font.Style, GraphicsUnit.Pixel);
            void Typography(Control control, float pixels) => control.Font = new Font("맑은 고딕", pixels, control.Font.Style, GraphicsUnit.Pixel);
            Typography(header.Controls[0], 36); Typography(header.Controls[1], 18);
            Typography(settingsHeading.Controls[0], 21); Typography(status.Controls[0], 21);
            Typography(_selectedName, 20); Typography(_condition, 20); Typography(Owned, 18);
            Typography(_start, 22); Typography(_stop, 22);
            Typography(_progressText, 20); Typography(_percentage, 20);
            Typography(_materials, 17); Typography(_collection, 14); Typography(_reload, 14);
            foreach (var label in new[] { _elapsed, _state, _cli, _character }) Typography(label, 17);
            foreach (Control label in progressLayout.Controls) if (label is Label && label != _progressText && label != _percentage) Typography(label, 14);
            foreach (var summary in new[] { summaries.Controls[0], summaries.Controls[1] })
                Typography(((TableLayoutPanel)summary.Controls[0]).GetControlFromPosition(0, 0)!, 21);
            // Match the reviewed 1200x900 preview at every supported window size.
            // Dashboard bounds scale independently of WinForms fonts and absolute rows.
            var metrics = Descendants(root).Prepend(root).Select(c => new PageMetric(c, c.Font.FontFamily.Name,
                c.Font.Size, c.Font.Style, c.Padding, c.Margin)).ToArray();
            var rows = metrics.SelectMany(m => m.Control is TableLayoutPanel t
                ? t.RowStyles.Cast<RowStyle>().Where(s => s.SizeType == SizeType.Absolute).Select(s => (Style: s, Size: s.Height))
                : Enumerable.Empty<(RowStyle Style, float Size)>()).ToArray();
            var columns = metrics.SelectMany(m => m.Control is TableLayoutPanel t
                ? t.ColumnStyles.Cast<ColumnStyle>().Where(s => s.SizeType == SizeType.Absolute).Select(s => (Style: s, Size: s.Width))
                : Enumerable.Empty<(ColumnStyle Style, float Size)>()).ToArray();
            var scaledFonts = new List<Font>();
            var fontCache = new Dictionary<(string Family, float Size, FontStyle Style), Font>();
            float lastScale = -1; int lastDpi = -1;
            bool fitting = false;
            Disposed += (_, _) => { foreach (var font in scaledFonts) font.Dispose(); };
            void FitPage()
            {
                if (fitting || ClientSize.Width < 1 || ClientSize.Height < 1) return;
                float scale = Math.Min(ClientSize.Width / 1004f, ClientSize.Height / 711f);
                float fontScale = Math.Min(ClientSize.Width / 1212f, ClientSize.Height / 858f);
                if (Math.Abs(scale - lastScale) < .001f && DeviceDpi == lastDpi) return;
                lastScale = scale; lastDpi = DeviceDpi;
                fitting = true;
                _layoutScale = scale;
                root.SuspendLayout();
                foreach (var metric in metrics)
                {
                    float size = Math.Max(metric.Size >= 18 ? 13f : 12f, MathF.Round(metric.Size * fontScale));
                    var key = (metric.Family, size, metric.Style);
                    if (!fontCache.TryGetValue(key, out var font)) { font = new Font(metric.Family, size, metric.Style, GraphicsUnit.Pixel); fontCache.Add(key, font); scaledFonts.Add(font); }
                    metric.Control.Font = font;
                    metric.Control.Padding = ScalePadding(metric.Padding, scale);
                    metric.Control.Margin = ScalePadding(metric.Margin, scale);
                }
                foreach (var row in rows) row.Style.Height = row.Size * scale;
                progressLayout.RowStyles[0].Height = progressLayout.GetControlFromPosition(0, 0)!.Font.Height + 3;
                foreach (var column in columns) column.Style.Width = column.Size * scale;
                _reload.Width = Math.Max(70, (int)(110 * scale));
                foreach (var field in Descendants(root).Where(c => c.Tag is string tag && tag == "production-field"))
                    LayoutField(field);
                root.ResumeLayout(true);
                fitting = false;
            }
            Resize += (_, _) => FitPage(); DpiChangedAfterParent += (_, _) => FitPage(); FitPage();
            Items.SelectedIndexChanged += (_, _) => SelectionChanged();
            Quantity.ValueChanged += (_, _) => SelectionChanged();
            Facility.SelectedIndexChanged += (_, _) =>
            {
                UpdateFacilityTabs();
                Filter();
            };
            _condition.Text = "목록 확인 전"; Owned.Text = "—"; UpdateExecution();
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
                string title = facility.Replace(" 시설", "");
                var button = PageButton(title, () =>
                {
                    if (Facility.Items.Contains(facility))
                        Facility.SelectedItem = facility;
                });
                button.AccessibleName = "가공 시설 " + title;
                button.Margin = new Padding(2);
                button.Font = new Font("맑은 고딕", 11f, FontStyle.Bold);
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
        {
            if (!string.IsNullOrWhiteSpace(recipe.FacilityName) &&
                AlteringPlan.Facilities.Contains(recipe.FacilityName))
                return recipe.FacilityName;

            // Older CLI builds may omit FacilityName. Keep the fallback deliberately
            // conservative; exact CLI FacilityName remains authoritative.
            string output = new AlteringPlan(
                AlteringPlan.Facilities[0], recipe.DisplayName, 1, recipe.ProducedPerWork, false).OutputName;

            if (output.Contains("목재", StringComparison.Ordinal) ||
                output.Contains("원목", StringComparison.Ordinal) ||
                output.Contains("통나무", StringComparison.Ordinal))
                return "목재 가공 시설";
            if (output.Contains("괴", StringComparison.Ordinal) ||
                output.Contains("철", StringComparison.Ordinal) ||
                output.Contains("금속", StringComparison.Ordinal))
                return "금속 가공 시설";
            if (output.Contains("가죽", StringComparison.Ordinal) ||
                output.Contains("피혁", StringComparison.Ordinal))
                return "가죽 가공 시설";
            if (output.Contains("옷감", StringComparison.Ordinal) ||
                output.Contains("실크", StringComparison.Ordinal) ||
                output.Contains("천", StringComparison.Ordinal))
                return "옷감 가공 시설";
            if (output.Contains("물약", StringComparison.Ordinal) ||
                output.Contains("비약", StringComparison.Ordinal) ||
                output.Contains("항마석", StringComparison.Ordinal))
                return "약품 가공 시설";
            if (output.Contains("식재료", StringComparison.Ordinal) ||
                output.Contains("요리", StringComparison.Ordinal) ||
                output.Contains("밀가루", StringComparison.Ordinal))
                return "식재료 가공 시설";

            return null;
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

        private static void StyleField(Control control)
        {
            control.Dock = DockStyle.Fill; control.BackColor = CardBg2; control.ForeColor = TitleText;
            control.Margin = new Padding(3, 5, 3, 5);
            control.Font = new Font("맑은 고딕", 13f);
            if (control is TextBox text) text.BorderStyle = BorderStyle.None;
            if (control is NumericUpDown number) number.BorderStyle = BorderStyle.None;
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
            if (_loading || (_loaded && !force) || _owner.AnyRunning) return;
            _loading = true; UpdateExecution();
            try
            {
                _choices = IsAltering
                    ? (await new AlteringCliData(_owner._cli).RecipesAsync(CancellationToken.None))
                        .GroupBy(x => (Facility: RecipeFacility(x), x.DisplayName))
                        .Select(g => (object)new RecipeChoice(g.First())).ToArray()
                    : (await new GatheringCliData(_owner._cli).CatalogAsync(CancellationToken.None))
                        .Select(x => (object)new GatheringChoice(x)).ToArray();
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
                Quantity.Value = Math.Clamp(saved.TargetQuantity, (int)Quantity.Minimum, (int)Quantity.Maximum);
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
            bool running = _owner.AnyRunning;
            bool available = Items.SelectedItem switch
            {
                GatheringChoice { Item.ToolOk: true } => true,
                RecipeChoice r => r.Recipe.Alterable || r.Recipe.MissingIngredients.Count > 0,
                _ => false
            };
            foreach (var field in new Control[] { Items, Quantity, _reload }) field.Enabled = !running && !_loading;
            Facility.Enabled = !running && !_loading;
            foreach (var tab in _facilityTabButtons.Values) tab.Enabled = !running && !_loading;
            _start.Enabled = !running && !_loading && available; _stop.Enabled = running;
            bool ownRun = _owner._activeMode == (IsAltering ? "가공" : "채집");
            bool ownResult = _owner._productionLastMode == (IsAltering ? "가공" : "채집") && _owner._productionDisplayName == SelectedName;
            long current = ownRun || ownResult ? _owner._productionCurrentQuantity : 0;
            int target = ownRun ? _owner._productionTargetQuantity : (int)Quantity.Value;
            int percent = target > 0 ? (int)Math.Clamp(current * 100 / target, 0, 100) : 0;
            _progressText.Text = $"{current:N0} / {target:N0}"; _percentage.Text = $"{percent}%"; _percent = percent; _progress.Invalidate();
            _progressText.AccessibleName = $"진행 수량 {current} / {target}, 진행률 {percent}%";
            var startedAt = _owner._dungeonStartedAt;
            var stoppedAt = _owner._dungeonStoppedAt;
            var elapsed = ownRun && startedAt.HasValue ? DateTime.Now - startedAt.Value
                : ownResult && stoppedAt.HasValue && startedAt.HasValue ? stoppedAt.Value - startedAt.Value : TimeSpan.Zero;
            _elapsed.Text = $"진행 시간\n{elapsed:hh\\:mm\\:ss}";
            _state.Text = "현재 상태\n" + (ownRun ? _owner._statusValue.Text : _loading ? "CLI 목록 조회 중" : ownResult && _owner._runError is not null ? _owner._runError : "대기 중");
            _cli.Text = "CLI 상태\n" + CliStatus; _character.Text = "캐릭터\n" + CharacterStatus;
            _cli.ForeColor = CliStatus == "정상" ? Green : Muted;
            _character.ForeColor = CharacterStatus == "확인됨" ? Green : Muted;
            _collection.Text = IsAltering
                ? ownRun && !string.IsNullOrWhiteSpace(_owner._productionProgressSummary)
                    ? _owner._productionProgressSummary
                    : !ownRun && !string.IsNullOrWhiteSpace(_owner._productionProgressSummary) &&
                      _owner._productionProgressSummary.StartsWith("이어하기 대기", StringComparison.Ordinal)
                        ? _owner._productionProgressSummary
                        : $"완료 대기 작업 수  {CompletedWorks?.ToString() ?? "—"}   ·   완료품 자동 수령  {(ownRun ? "작동 중" : "대기")}"
                : "목표 수량은 현재 보유량에서 추가로 수집할 수량입니다.";
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
