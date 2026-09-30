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
    private sealed record RecipeChoice(AlteringRecipe Recipe,int Ordinal)
    { public override string ToString()=>Recipe.DisplayName+(Ordinal>1?$" (제법 {Ordinal})":""); }
    private readonly CheckBox _useSource=new(){Text="가공 품목을 열고 재료를 찾아서 시작",AutoSize=true};
    private readonly ComboBox _facility=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Enabled=false};
    private readonly ComboBox _recipe=new(){Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList,Enabled=false};
    internal GatheringPlan? Plan { get; private set; }

    internal GatheringSettingsDialog(IReadOnlyList<GatherableItem> catalog,IReadOnlyList<AlteringRecipe>? recipes=null)
    {
        _choices = catalog.Select(x => new Choice(x)).ToArray();
        Text = "자동채집 설정"; ClientSize = new(650, 520); StartPosition = FormStartPosition.CenterParent;
        Font = new("맑은 고딕", 10); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 2, RowCount = 9 };
        layout.ColumnStyles.Add(new(SizeType.Absolute, 125)); layout.ColumnStyles.Add(new(SizeType.Percent, 100));
        for(int i = 0; i < 3; i++) layout.RowStyles.Add(new(SizeType.Absolute, 44));
        layout.RowStyles.Add(new(SizeType.Absolute,38));
        layout.RowStyles.Add(new(SizeType.Absolute,44)); layout.RowStyles.Add(new(SizeType.Absolute,44));
        layout.RowStyles.Add(new(SizeType.Absolute, 42)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 42));
        string[] captions = { "품목 검색", "채집 대상", "추가 채집 수량" };
        Control[] fields = { _search, _items, _quantity };
        for(int i = 0; i < 3; i++)
        {
            layout.Controls.Add(new Label { Text = captions[i], Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, i);
            layout.Controls.Add(fields[i], 1, i);
        }
        layout.Controls.Add(_useSource,0,3);layout.SetColumnSpan(_useSource,2);
        _facility.Items.AddRange(AlteringPlan.Facilities);_facility.SelectedIndex=0;
        var ordinals=new Dictionary<string,int>();
        _recipe.Items.AddRange((recipes??Array.Empty<AlteringRecipe>()).Select(r=>new RecipeChoice(r,ordinals[r.DisplayName]=ordinals.GetValueOrDefault(r.DisplayName)+1)).Cast<object>().ToArray());
        if(_recipe.Items.Count>0)_recipe.SelectedIndex=0;
        layout.Controls.Add(new Label{Text="시작 가공 시설",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,4);layout.Controls.Add(_facility,1,4);
        layout.Controls.Add(new Label{Text="재료를 담은 품목",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,5);layout.Controls.Add(_recipe,1,5);
        layout.Controls.Add(_status, 0, 6); layout.SetColumnSpan(_status, 2);
        var note = new Label { Dock = DockStyle.Fill, Text = "정령의 날개는 사용하지 않습니다. 장소 목록의 맨 위 항목으로 일반 이동합니다.\n위 선택을 끄면 대상 재료의 상세 또는 구하는 방법 화면을 열고 시작하세요.\n목표 수량 도달 시 정지합니다. 한 번의 보상으로 목표를 넘을 수 있습니다.\n낚시 품목은 현재 지원하지 않습니다." };
        layout.Controls.Add(note, 0, 7); layout.SetColumnSpan(note, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.Add(cancel); buttons.Controls.Add(_start);
        layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 2); Controls.Add(layout);
        AcceptButton = _start; CancelButton = cancel;
        _search.TextChanged += (_,_) => Filter();
        _items.SelectedIndexChanged += (_,_) => UpdateStatus();
        _useSource.CheckedChanged+=(_,_)=>{_facility.Enabled=_recipe.Enabled=_useSource.Checked;UpdateStatus();};
        _recipe.SelectedIndexChanged+=(_,_)=>UpdateStatus();
        _start.Click += (_,_) =>
        {
            if(_items.SelectedItem is not Choice choice || !choice.Item.ToolOk) return;
            AlteringPlan? source=null;
            if(_useSource.Checked)
            {
                if(_recipe.SelectedItem is not RecipeChoice recipe)return;
                source=new(_facility.SelectedItem!.ToString()!,recipe.Recipe.DisplayName,1,recipe.Recipe.ProducedPerWork,false,recipe.Ordinal);
            }
            Plan = new(choice.Item.DisplayName, (int)_quantity.Value){SourceRecipe=source}; Plan.Validate(); DialogResult = DialogResult.OK;
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
        _start.Enabled = choice?.Item.ToolOk == true && (!_useSource.Checked || _recipe.SelectedItem is RecipeChoice);
        _status.Text = choice is null ? "일치하는 품목이 없습니다." : choice.Item.ToolOk ?
            "현재 도구 조건: 채집 가능 · 날개 사용 0개" : "채집 도구가 없거나 내구도가 부족합니다.";
    }
}
