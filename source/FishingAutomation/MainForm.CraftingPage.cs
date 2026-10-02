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
        private readonly Button _food;
        private readonly Button _item;
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

        internal readonly ComboBox Items = new();
        internal readonly NumericUpDown Quantity = new();
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
                Padding = new Padding(18, 10, 18, 12),
                Margin = Padding.Empty,
                BackColor = WindowBg
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));

            var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
            header.Controls.Add(new Label
            {
                Text = "제작",
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 28f, FontStyle.Bold, GraphicsUnit.Pixel),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            header.Controls.Add(new Label
            {
                Text = "음식·아이템을 검색하고 최대 10회 퀘스트 단위로 자동 제작합니다.",
                Dock = DockStyle.Fill,
                ForeColor = Muted,
                Font = new Font("맑은 고딕", 16f, FontStyle.Regular, GraphicsUnit.Pixel),
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
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

            var left = Card();
            var form = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 5,
                Padding = new Padding(16, 10, 16, 10)
            };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 5; i++) form.RowStyles.Add(new RowStyle(SizeType.Percent, 20));

            var category = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            category.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            category.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            _food = Button("음식", () => SetCategory(CraftingCategory.Food));
            _item = Button("아이템", () => SetCategory(CraftingCategory.Item));
            _food.AccessibleName = "제작 음식";
            _item.AccessibleName = "제작 아이템";
            category.Controls.Add(_food, 0, 0);
            category.Controls.Add(_item, 1, 0);
            AddRow(form, 0, "분류", category);

            _search.PlaceholderText = "품목을 검색하세요";
            StyleField(_search);
            AddRow(form, 1, "품목 검색", _search);

            Items.DropDownStyle = ComboBoxStyle.DropDownList;
            Items.FlatStyle = FlatStyle.Flat;
            Items.BackColor = CardBg2;
            Items.ForeColor = TitleText;
            Items.Font = new Font("맑은 고딕", 12f);
            Items.AccessibleName = "제작 품목";
            AddRow(form, 2, "제작 품목", Items);

            Quantity.Minimum = 1;
            Quantity.Maximum = 1_000_000;
            Quantity.Value = 100;
            Quantity.BorderStyle = BorderStyle.None;
            Quantity.BackColor = CardBg2;
            Quantity.ForeColor = TitleText;
            Quantity.Font = new Font("맑은 고딕", 12f);
            Quantity.AccessibleName = "제작 목표 수량";
            AddRow(form, 3, "목표 수량", Quantity);

            _reload = Button("목록 새로고침", () => _ = LoadCatalogAsync(true));
            _reload.AccessibleName = "제작 목록 새로고침";
            AddRow(form, 4, "목록", _reload);
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
            summaryLayout.Controls.Add(Label("선택한 제작 품목", 19, true), 0, 0);
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
            executionLayout.Controls.Add(Label("▶  실행 상태", 20, true), 0, 0);

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
            _progress.Font = new Font("맑은 고딕", 24f, FontStyle.Bold, GraphicsUnit.Pixel);
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

            _search.TextChanged += (_, _) => Filter();
            Items.SelectedIndexChanged += (_, _) => SelectionChanged();
            Quantity.ValueChanged += (_, _) => SelectionChanged();
            SetCategory(CraftingCategory.Food);
            UpdateExecution();
        }

        private static Panel Card()
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = CardBg, Margin = new Padding(4) };
            panel.Paint += (_, e) => ControlPaint.DrawBorder(
                e.Graphics, panel.ClientRectangle, Line, ButtonBorderStyle.Solid);
            return panel;
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
            var button = new Button
            {
                Text = text,
                Dock = DockStyle.Fill,
                FlatStyle = FlatStyle.Flat,
                BackColor = CardBg2,
                ForeColor = TitleText,
                Font = new Font("맑은 고딕", 16f, FontStyle.Bold, GraphicsUnit.Pixel),
                Margin = new Padding(3),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Line;
            button.Click += (_, _) => action();
            return button;
        }

        private static void StyleField(TextBox box)
        {
            box.Dock = DockStyle.Fill;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = CardBg2;
            box.ForeColor = TitleText;
            box.Font = new Font("맑은 고딕", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
            box.Margin = new Padding(3, 6, 3, 6);
        }

        private static void AddRow(TableLayoutPanel form, int row, string caption, Control control)
        {
            form.Controls.Add(new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                ForeColor = Muted,
                Font = new Font("맑은 고딕", 15f, FontStyle.Regular, GraphicsUnit.Pixel),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, row);
            control.Dock = DockStyle.Fill;
            control.Margin = new Padding(3, 6, 3, 6);
            form.Controls.Add(control, 1, row);
        }

        private void SetCategory(CraftingCategory category)
        {
            Category = category;
            _food.BackColor = category == CraftingCategory.Food ? Accent : CardBg2;
            _item.BackColor = category == CraftingCategory.Item ? Accent : CardBg2;
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
