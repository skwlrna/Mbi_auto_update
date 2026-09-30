namespace FishingAutomation;

internal sealed class AlteringSettingsDialog : Form
{
    private sealed record Choice(AlteringRecipe Recipe, int Ordinal, bool Duplicate)
    {
        public override string ToString() => Recipe.DisplayName + (Duplicate ? $" (제법 {Ordinal})" : "") +
            (Recipe.Alterable ? " · 가공 가능" : " · 조건 확인 필요");
    }
    private readonly ComboBox _facility = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly ComboBox _item = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _filter = new() { Dock = DockStyle.Fill, PlaceholderText = "품목 검색" };
    private readonly NumericUpDown _quantity = new() { Minimum = 1, Maximum = 1000000, Value = 100, Dock = DockStyle.Fill };
    private readonly CheckBox _paid = new() { Text = "가공하러 가기 버튼의 정령의 날개 5개 사용", Checked = true, AutoSize = true };
    private readonly Label _summary = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Label _materials = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly Button _start = new() { Text = "이 설정으로 시작", AutoSize = true };
    private readonly Choice[] _choices;
    internal AlteringPlan? Plan { get; private set; }

    internal AlteringSettingsDialog(IReadOnlyList<AlteringRecipe> recipes)
    {
        Text = "자동 가공 설정"; StartPosition = FormStartPosition.CenterParent;
        ClientSize = new(650, 445); FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false; Font = new("맑은 고딕", 10);
        var ordinals = new Dictionary<string, int>();
        var counts = recipes.GroupBy(x => x.DisplayName).ToDictionary(x => x.Key, x => x.Count());
        _choices = recipes.Select(r => new Choice(r, ordinals[r.DisplayName] = ordinals.GetValueOrDefault(r.DisplayName) + 1,
            counts[r.DisplayName] > 1)).ToArray();
        _facility.Items.AddRange(AlteringPlan.Facilities); _facility.SelectedIndex = 0;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 125)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute, 50)); layout.RowStyles.Add(new(SizeType.Absolute, 60));
        layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 42));
        string[] captions = { "가공 시설", "품목 검색", "전체 가공 품목", "목표 생산 수량" };
        Control[] fields = { _facility, _filter, _item, _quantity };
        for (int i = 0; i < fields.Length; i++)
        {
            layout.Controls.Add(new Label { Text = captions[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            layout.Controls.Add(fields[i], 1, i);
        }
        layout.Controls.Add(_materials, 0, 4); layout.SetColumnSpan(_materials, 2);
        layout.Controls.Add(_summary, 0, 5); layout.SetColumnSpan(_summary, 2);
        var note = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        note.Controls.Add(_paid);
        note.Controls.Add(new Label { AutoSize = true, Text = "게임의 시설과 품목을 맞춰 선택하세요. 완료된 기존 작업은 먼저 수령합니다." });
        layout.Controls.Add(note, 0, 6); layout.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(_start);
        layout.Controls.Add(buttons, 0, 7); layout.SetColumnSpan(buttons, 2); Controls.Add(layout);
        CancelButton = cancel; AcceptButton = _start;
        _filter.TextChanged += (_, _) => Filter();
        _item.SelectedIndexChanged += (_, _) => UpdateDetails();
        _quantity.ValueChanged += (_, _) => UpdateDetails(); _paid.CheckedChanged += (_, _) => UpdateDetails();
        _start.Click += (_, _) =>
        {
            if (_item.SelectedItem is not Choice choice) return;
            Plan = new(_facility.SelectedItem!.ToString()!, choice.Recipe.DisplayName, (int)_quantity.Value,
                choice.Recipe.ProducedPerWork, _paid.Checked, choice.Ordinal);
            Plan.Validate(); DialogResult = DialogResult.OK;
        };
        Filter();
    }
    private static string ReasonText(string? reason) => reason switch
    {
        "not_enough_ingredient" => "재료 부족",
        "insufficient_facility_level" => "시설 레벨 부족",
        "ingredient_locked" => "재료 잠금 확인 필요",
        "insufficient_transfer_cost" => "이동 비용 부족",
        _ => "게임에서 조건 확인 필요"
    };
    private void Filter()
    {
        _item.Items.Clear();
        _item.Items.AddRange(_choices.Where(x => x.Recipe.DisplayName.Contains(_filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)).Cast<object>().ToArray());
        if (_item.Items.Count > 0) _item.SelectedIndex = 0;
        UpdateDetails();
    }
    private void UpdateDetails()
    {
        if (_item.SelectedItem is not Choice choice) { _start.Enabled = false; _summary.Text = "일치하는 품목이 없습니다."; return; }
        var r = choice.Recipe;
        var plan = new AlteringPlan(_facility.SelectedItem!.ToString()!, r.DisplayName, (int)_quantity.Value, r.ProducedPerWork, _paid.Checked, choice.Ordinal);
        _materials.Text = r.Alterable ? "현재 조건: 가공 가능" : "조건: " + ReasonText(r.Reason) + " · " + string.Join(", ", r.MissingIngredients.Select(x => $"{x.DisplayName} {x.Owned}/{x.Required}"));
        _summary.Text = $"1회 {r.ProducedPerWork}개 · {plan.RequiredWorks}회 등록 → 최소 {plan.ExpectedQuantity}개 생산\n정령의 날개 사용 상한: {plan.MaximumWings}개 (대성공 보상은 추가될 수 있습니다)";
        _start.Enabled = true;
    }
}
