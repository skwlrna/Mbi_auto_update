using FishingAutomation;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Drawing.Imaging;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            var fake = new FakeCli();
            MabinogiMobileCli? connector = null;
            using var form = new MainForm(log => connector = new MabinogiMobileCli(log, true, fake.Query, fake.Action)
                { RunFilteredQuery = (a, ct) => fake.Query(a[0], ct) });
            form.Show(); Pump();
            var controls = All(form).ToArray();
            var gathering = controls.Single(x => x.Name == "GatheringPage");
            var altering = controls.Single(x => x.Name == "AlteringPage");
            var crafting = controls.Single(x => x.Name == "CraftingPage");
            void Menu(string name) => controls.OfType<Button>().Single(x => x.Text == name && !Inside(x, gathering) && !Inside(x, altering) && !Inside(x, crafting)).PerformClick();
            void Exclusive(bool isAltering) => Check(altering.Visible == isAltering && gathering.Visible != isAltering, "only selected production page visible");
            Menu("자동 가공"); PumpUntil(() => All(altering).OfType<ListBox>().Single(x => x.AccessibleName == "가공 제법").Items.Count > 0);
            Exclusive(true);
            Check(!All(gathering).Any(x => x.Visible), "home to altering does not require gathering first");
            Menu("홈"); Check(!altering.Visible && !gathering.Visible, "home restores existing central content");
            Menu("자동 채집"); PumpUntil(() => All(gathering).OfType<ListBox>().Any(x => x.Items.Count == 3)); Exclusive(false);
            var gi = All(gathering).OfType<ListBox>().Single();
            var gq = All(gathering).OfType<NumericUpDown>().Single();
            Check(!gq.Controls.Cast<Control>().Any(x =>
                    x.Visible && x.GetType().Name.Contains("UpDownButtons", StringComparison.Ordinal)),
                "gathering target quantity hides numeric up/down arrow buttons");
            Check(gi.Items.Count == 3 && gi.Items[1]!.ToString() == "상급 통나무+", "gathering dropdown uses actual CLI catalog including plus name");
            gi.SelectedIndex = 2; Pump();
            var gs = All(gathering).OfType<Button>().Single(x => x.AccessibleName == "자동 채집 시작");
            Check(!gs.Enabled, "ToolOk false disables start");
            var inspect = All(gathering).OfType<Button>().Single(x => x.AccessibleName == "CLI 검사");
            Check(inspect.Enabled, "read-only inspection remains available with unavailable tool");
            var pendingDiagnostic = new TaskCompletionSource<CliProcessOutput>();
            connector!.RunFilteredQuery = (a, ct) => pendingDiagnostic.Task;
            inspect.PerformClick();
            Check(Field<bool>(form, "_gatheringDiagnosticRunning") && !inspect.Enabled && !gs.Enabled,
                "CLI diagnostic locks start and repeat inspection on ZIP UI");
            typeof(MainForm).GetMethod("StartSelected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            Check(fake.Actions.Count == 0, "F9 during inspection cannot trigger paid gathering");
            Check(!All(gathering).OfType<Button>().Single(x => x.AccessibleName == "정지").Enabled,
                "inspection does not expose a stop action");
            pendingDiagnostic.SetResult(new CliProcessOutput(0, "{\"items\":[]}", ""));
            PumpUntil(() => !Field<bool>(form, "_gatheringDiagnosticRunning"));
            Check(fake.Actions.Count == 0 && Field<string?>(form, "_activeMode") is null,
                "ZIP UI inspection completes without action commands");
            connector.RunFilteredQuery = (a, ct) => fake.Query(a[0], ct);
            gi.SelectedIndex = 1; gq.Value = 5;
            Menu("자동 가공"); Exclusive(true);
            var ai = All(altering).OfType<ListBox>().Single(x => x.AccessibleName == "가공 제법");
            var aq = All(altering).OfType<NumericUpDown>().Single();
            var facilityTabs = All(altering).OfType<Button>()
                .Where(x => x.AccessibleName?.StartsWith("가공 시설 ", StringComparison.Ordinal) == true).ToArray();
            Check(facilityTabs.Length == 6, "automatic altering exposes six large facility tabs");
            var alterSearch = All(altering).OfType<TextBox>().Single(x => x.PlaceholderText.Contains("품목", StringComparison.Ordinal));
            Check(alterSearch.Visible, "automatic altering keeps facility tabs and adds item search");
            Check(!aq.Controls.Cast<Control>().Any(x =>
                    x.Visible && x.GetType().Name.Contains("UpDownButtons", StringComparison.Ordinal)),
                "target quantity hides numeric up/down arrow buttons");

            Check(ai.Items.Count == 1 && ai.Items[0]!.ToString() == "목재+",
                "default wood facility tab shows only wood recipes");
            facilityTabs.Single(x => x.Text == "금속").PerformClick(); Pump();
            Check(ai.Items.Count == 2 && ai.Items.Cast<object>().All(x => x.ToString() is "철괴" or "강철괴"),
                "metal facility tab filters recipe list to metal recipes");
            alterSearch.Text = "강철"; Pump();
            Check(ai.Items.Count == 1 && ai.Items[0]!.ToString() == "강철괴",
                "automatic altering search filters inside the selected facility tab");
            alterSearch.Text = ""; Pump();
            Check(ai.Items.Count == 2 && ai.Items.Cast<object>().All(x => x.ToString() is "철괴" or "강철괴"),
                "clearing altering search restores the selected facility list");
            ai.SelectedIndex = 1;
            Check(All(altering).OfType<Button>().Single(x => x.AccessibleName == "자동 가공 시작").Enabled,
                "missing-material recipe remains startable for recursive resolution");

            facilityTabs.Single(x => x.Text == "약품").PerformClick(); Pump();
            Check(ai.Items.Count == 3 &&
                  ai.Items.Cast<object>().Select(x => x.ToString()).SequenceEqual(
                      new[] { "새록 버섯 진액", "튼튼 버섯 가루", "불꽃의 결정(석양 나비)" }),
                "medicine facility tab shows medicine processing recipes");

            facilityTabs.Single(x => x.Text == "식재료").PerformClick(); Pump();
            Check(ai.Items.Count == 5 &&
                  ai.Items.Cast<object>().Select(x => x.ToString()).SequenceEqual(
                      new[] { "마요네즈", "밀가루", "치즈", "면", "생크림" }),
                "food facility tab shows multiple food-processing recipes, not only flour");

            facilityTabs.Single(x => x.Text == "목재").PerformClick(); Pump();
            ai.SelectedIndex = 0; aq.Value = 5;
            Menu("자동 채집"); Exclusive(false);
            Check(gi.SelectedIndex == 1 && gq.Value == 5, "switching preserves each page's independent settings");
            var selectedGather = Invoke<GatheringPlan>(form, "SelectedGatheringPlan");
            Check(selectedGather.DisplayName == "상급 통나무+" && selectedGather.TargetQuantity == 5,
                "main gathering selection and numeric quantity map to the free-screen plan");

            Menu("자동 가공"); Exclusive(true);
            var selectedAlter = Invoke<AlteringPlan>(form, "SelectedAlteringPlan");
            Check(selectedAlter.DisplayName == "목재+" && selectedAlter.FacilityName == "목재 가공 시설" &&
                  selectedAlter.TargetQuantity == 5 && !selectedAlter.AllowPaidButton && selectedAlter.MaximumWings == 0,
                "selected facility tab and recipe map to the zero-wing altering plan");
            Check(fake.Actions.Count == 0, "production UI performs no CLI action commands while configuring plans");
            foreach (var page in new[] { altering, gathering })
            {
                Check(!All(page).OfType<ComboBox>().Any(), page.Name + " has no visible execution/status combo boxes");
                Check(!All(page).OfType<Label>().Any(x => x.Visible && x.Text.Contains("현재 보유", StringComparison.Ordinal)),
                    page.Name + " removes current-owned quantity card");
                Check(All(page).OfType<TextBox>().Any(x => x.AccessibleName?.EndsWith(" 로그", StringComparison.Ordinal) == true),
                    page.Name + " shows log on the right side");
            }

            Menu("제작"); PumpUntil(() => All(crafting).OfType<ListBox>().Single().Items.Count > 0);
            Check(crafting.Visible && !altering.Visible && !gathering.Visible,
                "crafting opens as an independent production page");
            Check(!All(crafting).OfType<ComboBox>().Any(), "crafting has no visible execution/status combo boxes");
            Check(!All(crafting).OfType<Label>().Any(x => x.Visible && x.Text.Contains("현재 보유", StringComparison.Ordinal)),
                "crafting removes current-owned quantity card");
            Check(All(crafting).OfType<TextBox>().Any(x => x.AccessibleName == "제작 로그"),
                "crafting shows log on the right side");
            var craftItems = All(crafting).OfType<ListBox>().Single();
            var craftSearch = All(crafting).OfType<TextBox>().Single(x => x.PlaceholderText.Contains("품목", StringComparison.Ordinal));
            Check(craftItems.DrawMode == DrawMode.OwnerDrawFixed &&
                  craftItems.BorderStyle == BorderStyle.None &&
                  craftItems.BackColor == ai.BackColor &&
                  craftItems.Parent?.BackColor == ai.Parent?.BackColor,
                "crafting text-only item list matches automatic-altering owner-draw color surface");
            Check(craftSearch.BorderStyle == BorderStyle.None &&
                  craftItems.Parent is Panel &&
                  craftSearch.Parent is Panel,
                "crafting search/item/quantity fields use dashboard card surfaces");
            var craftItemTab = All(crafting).OfType<Button>().Single(x => x.AccessibleName == "제작 아이템");
            var craftFoodTab = All(crafting).OfType<Button>().Single(x => x.AccessibleName == "제작 음식");
            Check(craftItemTab.Left < craftFoodTab.Left &&
                  craftItemTab.Parent == craftFoodTab.Parent &&
                  craftFoodTab.BackColor != craftItemTab.BackColor,
                "crafting uses altering-style side-by-side item and food category tabs");
            Check(craftItems.Items.Cast<object>().Any(x => x.ToString() == "야채볶음"),
                "food crafting catalog includes CLI food item");
            All(crafting).OfType<Button>().Single(x => x.AccessibleName == "제작 아이템").PerformClick(); Pump();
            Check(craftItems.Items.Cast<object>().Any(x => x.ToString() == "상급 회복 물약"),
                "item crafting category includes CLI item");
            Check(craftItems.Items.Cast<object>().Any(x => x.ToString() == "분류 없는 결과물"),
                "unknown craftable category is not omitted from item UI");
            craftSearch.Text = "회복"; Pump();
            Check(craftItems.Items.Count == 1 && craftItems.Items[0]!.ToString() == "상급 회복 물약",
                "crafting item search filters the complete catalog");
            var craftingQuantity = All(crafting).OfType<NumericUpDown>().Single();
            Check(!craftingQuantity.Controls.Cast<Control>().Any(x =>
                    x.Visible && x.GetType().Name.Contains("UpDownButtons", StringComparison.Ordinal)),
                "crafting target quantity matches production UI without arrow buttons");
            craftingQuantity.Value = 23;
            var craftPlan = Invoke<CraftingPlan>(form, "SelectedCraftingPlan");
            Check(craftPlan.Category == CraftingCategory.Item && craftPlan.TargetQuantity == 23 && craftPlan.ProducedPerCraft == 5,
                "crafting selection preserves category, quantity and per-craft yield");
            if (args.Length > 0) { Directory.CreateDirectory(args[0]); Save(form, Path.Combine(args[0], "crafting.png")); }
            Check(fake.Actions.Count == 0, "crafting catalog and search remain read-only");
            fake.DuplicateCrafting = true;
            craftSearch.Text = "";
            All(crafting).OfType<Button>().Single(x => x.AccessibleName == "제작 목록 새로고침").PerformClick();
            PumpUntil(() => craftItems.Items.Count == 2);
            Check(!All(crafting).OfType<Button>().Single(x => x.AccessibleName == "제작 시작").Enabled,
                "duplicate crafting names remain listed but disable automatic start");
            try { Invoke<CraftingPlan>(form, "SelectedCraftingPlan"); throw new Exception("duplicate crafting selection accepted"); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            { Check(true, "F9 plan selection also rejects duplicate crafting names"); }

            foreach (string mode in new[] { "낚시", "던전", "어비스" })
            {
                Menu(mode);
                Check(!altering.Visible && !gathering.Visible && !crafting.Visible, "legacy page preserved and untouched: " + mode);
            }
            fake.Fail = true; Menu("자동 채집");
            All(gathering).OfType<Button>().Single(x => x.Text == "목록 새로고침").PerformClick();
            PumpUntil(() => gi.Items.Count == 0);
            Check(!gs.Enabled && gi.Items.Count == 0, "CLI failure does not fabricate catalog or allow start");
            fake.Fail = false;
            All(gathering).OfType<Button>().Single(x => x.Text == "목록 새로고침").PerformClick(); PumpUntil(() => gi.Items.Count == 3);
            Check(gs.Enabled, "catalog can recover after reconnect");
            foreach (var size in new[] { new Size(940, 700), new Size(1200, 900) })
            {
                form.ClientSize = size; Pump();
                Menu("제작"); Pump();
                float craftingScale = Math.Min(crafting.Width / 1212f, crafting.Height / 858f);
                var craftingTitle = All(crafting).OfType<Label>().Single(x => x.Text == "제작");
                var craftingInput = All(crafting).OfType<NumericUpDown>().Single();
                Check(craftingTitle.Font.Unit == GraphicsUnit.Pixel &&
                      craftingTitle.Font.Size == Math.Max(13f, MathF.Round(36 * craftingScale)) &&
                      craftingInput.Font.Unit == GraphicsUnit.Pixel &&
                      craftingInput.Font.Size == Math.Max(13f, MathF.Round(18 * craftingScale)),
                    "crafting title and content match automatic-altering typography at " + size);
                var craftingTabs = All(crafting).OfType<Button>()
                    .Where(x => x.AccessibleName is "제작 아이템" or "제작 음식").ToArray();
                Check(craftingTabs.Length == 2 &&
                      craftingTabs.All(x => x.Font.Unit == GraphicsUnit.Pixel &&
                          x.Font.Size == Math.Max(13f, MathF.Round(18 * craftingScale))),
                    "crafting category tabs match automatic-altering font scale at " + size);
                var craftingProgress = All(crafting).OfType<Label>().Single(x => x.Text.StartsWith("진행 수량  ", StringComparison.Ordinal));
                Check(craftingProgress.Height >= craftingProgress.Font.Height && FullyContained(craftingProgress, crafting),
                    "crafting progress is fully visible at " + size);
                Check(All(crafting).OfType<Button>().Where(x => x.AccessibleName is "제작 시작" or "제작 정지")
                        .All(x => x.Height >= x.Font.Height && FullyContained(x, crafting)),
                    "crafting start and stop controls fit at " + size);
                if (args.Length > 0) Save(form, Path.Combine(args[0], "crafting-" + size.Width + ".png"));
                Menu("자동 채집"); Pump();
                Check(gathering.Right <= form.ClientSize.Width && gathering.Bottom < form.ClientSize.Height,
                    "production content fits at " + size);
                Check(All(gathering).Where(x => x.Visible).All(x => x.Width > 0 && x.Height > 0), "native controls remain visible at " + size);
                foreach (var page in new[] { gathering, altering })
                {
                    Menu(page == gathering ? "자동 채집" : "자동 가공"); Pump();
                    float referenceScale = Math.Min(page.Width / 1212f, page.Height / 858f);
                    var title = All(page).OfType<Label>().Single(x => x.Text == (page == gathering ? "자동 채집" : "자동 가공"));
                    var input = All(page).OfType<NumericUpDown>().Single();
                    Check(title.Font.Unit == GraphicsUnit.Pixel && title.Font.Size == Math.Max(13f, MathF.Round(36 * referenceScale))
                        && input.Font.Unit == GraphicsUnit.Pixel && input.Font.Size == Math.Max(13f, MathF.Round(18 * referenceScale)),
                        page.Name + " title and content match abyss typography at " + size);
                    var values = All(page).OfType<Label>().Where(x => x.Visible &&
                        (x.Text.StartsWith("현재 상태", StringComparison.Ordinal) ||
                         x.Text.StartsWith("현재 단계", StringComparison.Ordinal) ||
                         x.Text.StartsWith("진행 수량", StringComparison.Ordinal) ||
                         x.Text.StartsWith("진행률", StringComparison.Ordinal) ||
                         x.Text.StartsWith("경과 시간", StringComparison.Ordinal) ||
                         x.Text.StartsWith("오류 횟수", StringComparison.Ordinal) ||
                         x.Text.StartsWith("CLI 상태", StringComparison.Ordinal))).ToArray();
                    Check(values.Length >= 6 && values.All(x => x.Height >= x.Font.Height),
                        page.Name + " progress and status text fits at " + size);
                    Check(values.All(x => FullyContained(x, page)), page.Name + " status cards do not extend beyond parent rows at " + size);
                    if (args.Length > 0) { Directory.CreateDirectory(args[0]); Save(form, Path.Combine(args[0], page.Name + "-" + size.Width + ".png")); }
                }
            }
            if (args.Length > 0)
            {
                Directory.CreateDirectory(args[0]); form.ClientSize = new Size(1200, 900);
                gi.SelectedIndex = 0; gq.Value = 100; Menu("자동 채집");
                InvokeTask(form, "RefreshProductionStateAsync", gathering); Pump(); Save(form, Path.Combine(args[0], "automatic-gathering.png"));
                Menu("자동 가공"); facilityTabs.Single(x => x.Text == "목재").PerformClick(); Pump(); ai.SelectedIndex = 0; aq.Value = 50;
                InvokeTask(form, "RefreshProductionStateAsync", altering); Pump(); Save(form, Path.Combine(args[0], "automatic-altering.png"));
                Menu("홈"); Pump(); Save(form, Path.Combine(args[0], "home.png"));
            }
            form.Close(); Console.WriteLine($"PASS {_checks} production UI checks"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static bool Inside(Control control, Control page)
    { for(var p=control.Parent; p is not null; p=p.Parent) if(p==page) return true; return false; }
    private static bool FullyContained(Control control, Control page)
    {
        for (var child = control; child != page && child.Parent != null; child = child.Parent)
            if (child.Left < 0 || child.Top < 0 || child.Right > child.Parent.ClientSize.Width || child.Bottom > child.Parent.ClientSize.Height) return false;
        return true;
    }
    private static IEnumerable<Control> All(Control root)
    { foreach(Control c in root.Controls) { yield return c; foreach(var child in All(c)) yield return child; } }
    private static T Field<T>(object target, string name) => (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target)!;
    private static T Invoke<T>(object target, string name)
        => (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
    private static void InvokeTask(object target, string name, object arg)
    { var task=(Task)target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,new[]{arg})!; PumpUntil(()=>task.IsCompleted); task.GetAwaiter().GetResult(); }
    private static void Check(bool ok, string message)
    { if(!ok) throw new Exception(message); _checks++; Console.WriteLine("PASS "+message); }
    private static void Pump() { Application.DoEvents(); Thread.Sleep(30); Application.DoEvents(); }
    private static void PumpUntil(Func<bool> condition)
    { var until=DateTime.UtcNow.AddSeconds(20); while(!condition()) { if(DateTime.UtcNow>until) throw new TimeoutException("UI condition timeout"); Pump(); } Pump(); }
    private static void Save(Form form, string path)
    { using var bitmap=new Bitmap(form.Width,form.Height); form.DrawToBitmap(bitmap,form.ClientRectangle); bitmap.Save(path,ImageFormat.Png); }
}

internal sealed class FakeCli
{
    internal readonly List<(string Command,string? Name)> Actions = new();
    internal bool Fail, Gain = true;
    internal bool DuplicateCrafting;
    private bool _gathering;
    private long _logs = 38, _ingots = 12, _wood = 20;
    private string? _work;
    public Task<CliProcessOutput> Query(string command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(Fail) return Task.FromResult(new CliProcessOutput(5,"{\"pipe\":\"disconnected\"}",""));
        if(command == "get_craftable_items" && DuplicateCrafting)
            return Task.FromResult(new CliProcessOutput(0,
                "{\"items\":[{\"DisplayName\":\"동일 이름\",\"Craftable\":true},{\"DisplayName\":\"동일 이름\",\"Craftable\":false}]}", ""));
        if(command=="get_items" && _gathering && Gain) _logs+=3;
        object data = command switch
        {
            "status" => new {pipe="connected"},
            "capabilities" => new { commands = new[]{"get_my_info","get_currencies","get_gatherable_items","get_craftable_items","get_activity","get_inventory","get_items","execute_gathering","stop_action","get_alterable_items","get_altering_works","execute_altering","complete_altering_work"}.Select(x=>new{Command=x,Metadata=new{requiresConfirm=true}}).ToArray() },
            "get_my_info" => new {CharacterId="ui-test",CharacterName="테스트",RealmName="테스트 서버"},
            "get_currencies" => new[]{new{DisplayName="정령의 날개",Amount=105455},new{DisplayName="골드",Amount=5000}},
            "get_gatherable_items" => new {items=new[]{new{DisplayName="철 광석",ToolOk=true},new{DisplayName="상급 통나무+",ToolOk=true},new{DisplayName="가죽",ToolOk=false}}},
            "get_craftable_items" => new {craftingUnlocked=true,items=new object[]{
                new{DisplayName="야채볶음",Craftable=false,ProducedPerCraft=1,Category="음식 제작대",MissingIngredients=new[]{new{DisplayName="감자",Required=8L,Owned=6L}}},
                new{DisplayName="감자 샐러드",Craftable=true,ProducedPerCraft=1,Category="음식 제작대",MissingIngredients=Array.Empty<object>()},
                new{DisplayName="상급 회복 물약",Craftable=true,ProducedPerCraft=5,Category="아이템",MissingIngredients=Array.Empty<object>()},
                new{DisplayName="분류 없는 결과물",Craftable=true,ProducedPerCraft=2,MissingIngredients=Array.Empty<object>()}
            }},
            "get_alterable_items" => new {items=new object[]{
                new{DisplayName="철괴",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>(),FacilityName="금속 가공 시설"},
                new{DisplayName="목재+",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>(),FacilityName="목재 가공 시설"},
                new{DisplayName="철괴",Alterable=false,ProducedPerWork=7,Reason=(string?)"material_shortage_changed",MissingIngredients=new[]{new{DisplayName="철 광석",Required=10L,Owned=0L}},FacilityName="금속 가공 시설"},
                new{DisplayName="강철괴",Alterable=false,ProducedPerWork=3,Reason=(string?)"material_shortage_changed",MissingIngredients=new[]{new{DisplayName="철괴",Required=3L,Owned=0L}},FacilityName="금속 가공 시설"},
                new{DisplayName="새록 버섯 진액",Alterable=true,ProducedPerWork=5,Reason=(string?)null,MissingIngredients=Array.Empty<object>()},
                new{DisplayName="튼튼 버섯 가루",Alterable=true,ProducedPerWork=5,Reason=(string?)null,MissingIngredients=Array.Empty<object>()},
                new{DisplayName="불꽃의 결정(석양 나비)",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>()},
                new{DisplayName="마요네즈",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>()},
                new{DisplayName="밀가루",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>()},
                new{DisplayName="치즈",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>()},
                new{DisplayName="면",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>()},
                new{DisplayName="생크림",Alterable=true,ProducedPerWork=3,Reason=(string?)null,MissingIngredients=Array.Empty<object>()}
            }},
            "get_items" => new[]{
                new{DisplayName="철 광석",Count=184L,Location="inventory"},
                new{DisplayName="상급 통나무+",Count=_logs,Location="inventory"},
                new{DisplayName="철괴",Count=_ingots,Location="inventory"},
                new{DisplayName="목재+",Count=_wood,Location="inventory"},
                new{DisplayName="야채볶음",Count=2L,Location="inventory"},
                new{DisplayName="감자 샐러드",Count=1L,Location="inventory"},
                new{DisplayName="상급 회복 물약",Count=3L,Location="inventory"},
                new{DisplayName="분류 없는 결과물",Count=1L,Location="inventory"}},
            "get_altering_works" => new{completedCount=_work is null?0:1,works=_work is null?Array.Empty<object>():new object[]{new{DisplayName=_work,FacilityName="목재 가공 시설",State="Completed",IsCompleted=true,RemainingSeconds=0}}},
            "get_inventory" => new{CurrentInventoryWeightAsDecimal=1,MaxInventoryWeightAsDecimal=100},
            "get_activity" => new{IsDead=false,IsReviving=false,IsInCombat=false,IsAutoPlaying=false,IsAutoTraveling=false,IsDialoguePlaying=false,IsWaitingForSelection=false,Dungeon=new{State="NotInDungeon"},Battlefield=new{IsInBattleField=false},Tutorial=new{IsPlaying=false},Scenario=new{IsInScenario=false},Performance=new{IsPlaying=false},Mode=new{IsPlayingMiniGame=false,IsHousingEditMode=false,MainButtonState=_gathering?"Stop":"Compass"},Interaction=new{HasTarget=_gathering,AvailableInteractionType=_gathering?"Gathering":"None",LastRunningInteractionType=_gathering?"Gathering":"None"}},
            _ => throw new Exception("Unexpected query "+command)
        };
        return Task.FromResult(new CliProcessOutput(0,JsonSerializer.Serialize(data),""));
    }
    public Task<CliProcessOutput> Action(IReadOnlyList<string> args, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); string? name=null;
        if(args.Count>1) { using var json=JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(args[1][7..])));name=json.RootElement.GetProperty("displayName").GetString(); }
        Actions.Add((args[0],name));
        switch(args[0])
        {
            case "execute_gathering": _gathering=true; break;
            case "stop_action": _gathering=false; break;
            case "execute_altering": _work=name; break;
            case "complete_altering_work": if(_work=="목재+")_wood+=3;else _ingots+=3;_work=null;break;
            default:throw new Exception("Unexpected action "+args[0]);
        }
        return Task.FromResult(new CliProcessOutput(0,"{\"result\":\"accepted\"}",""));
    }
}
