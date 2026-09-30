namespace FishingAutomation;

internal sealed class GatheringSettingsDialog : Form
{
    private sealed record Choice(GatherableItem Item)
    {
        public override string ToString() => Item.DisplayName + (Item.ToolOk ? "" : " · 도구 확인 필요");
    }
    private readonly TextBox _search = new() { Dock = DockStyle.Fill, PlaceholderText = "철 광석, 통나무 등 품목 검색" };
    private readonly ComboBox _items = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _quantity = new() { Dock = DockStyle.Fill, Minimum = 1, Maximum = 1000000, Value = 100 };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Button _start = new() { Text = "이 설정으로 시작", AutoSize = true };
    private readonly Choice[] _choices;
    internal GatheringPlan? Plan { get; private set; }

    internal GatheringSettingsDialog(IReadOnlyList<GatherableItem> catalog)
    {
        _choices = catalog.Select(x => new Choice(x)).ToArray();
        Text = "자동채집 설정"; ClientSize = new(600, 330); StartPosition = FormStartPosition.CenterParent;
        Font = new("맑은 고딕", 10); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 2, RowCount = 6 };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 125)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for(int i = 0; i < 3; i++) layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        string[] captions = { "품목 검색", "채집 대상", "추가 채집 수량" };
        Control[] fields = { _search, _items, _quantity };
        for(int i = 0; i < 3; i++)
        {
            layout.Controls.Add(new Label { Text = captions[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            layout.Controls.Add(fields[i], 1, i);
        }
        layout.Controls.Add(_status, 0, 3); layout.SetColumnSpan(_status, 2);
        var note = new Label { Dock = DockStyle.Fill, Text = "정령의 날개는 사용하지 않습니다. 구하는 방법의 추천 채집지로 이동합니다.\n목표 수량 도달 시 정지합니다. 한 번의 채집 보상으로 목표를 넘을 수 있습니다.\n낚시 품목은 현재 지원하지 않습니다." };
        layout.Controls.Add(note, 0, 4); layout.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(_start);
        layout.Controls.Add(buttons, 0, 5); layout.SetColumnSpan(buttons, 2); Controls.Add(layout);
        AcceptButton = _start; CancelButton = cancel;
        _search.TextChanged += (_,_) => Filter();
        _items.SelectedIndexChanged += (_,_) => UpdateStatus();
        _start.Click += (_,_) =>
        {
            if(_items.SelectedItem is not Choice choice || !choice.Item.ToolOk) return;
            Plan = new(choice.Item.DisplayName, (int)_quantity.Value); Plan.Validate(); DialogResult = DialogResult.OK;
        };
        Filter();
    }
    private void Filter()
    {
        _items.Items.Clear();
        _items.Items.AddRange(_choices.Where(x => x.Item.DisplayName.Contains(_search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
        if(_items.Items.Count > 0) _items.SelectedIndex = 0;
        UpdateStatus();
    }
    private void UpdateStatus()
    {
        var choice = _items.SelectedItem as Choice;
        _start.Enabled = choice?.Item.ToolOk == true;
        _status.Text = choice is null ? "일치하는 품목이 없습니다." : choice.Item.ToolOk ?
            "현재 도구 조건: 채집 가능 · 날개 사용 0개" : "채집 도구가 없거나 내구도가 부족합니다.";
    }
}
