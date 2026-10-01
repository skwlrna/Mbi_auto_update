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
            using var form = new MainForm(log => new MabinogiMobileCli(log, true, fake.Query, fake.Action));
            form.Show(); Pump();
            var controls = All(form).ToArray();
            var gathering = controls.Single(x => x.Name == "GatheringPage");
            var altering = controls.Single(x => x.Name == "AlteringPage");
            void Menu(string name) => controls.OfType<Button>().Single(x => x.Text == name && !Inside(x, gathering) && !Inside(x, altering)).PerformClick();
            void Exclusive(bool isAltering) => Check(altering.Visible == isAltering && gathering.Visible != isAltering, "only selected production page visible");
            Menu("자동 가공"); PumpUntil(() => All(altering).OfType<ComboBox>().Single(x => x.AccessibleName == "가공 제법").Items.Count > 0);
            Exclusive(true);
            Check(!All(gathering).Any(x => x.Visible), "home to altering does not require gathering first");
            Menu("홈"); Check(!altering.Visible && !gathering.Visible, "home restores existing central content");
            Menu("자동 채집"); PumpUntil(() => All(gathering).OfType<ComboBox>().Any(x => x.Items.Count == 3)); Exclusive(false);
            var gi = All(gathering).OfType<ComboBox>().Single();
            var gq = All(gathering).OfType<NumericUpDown>().Single();
            Check(gi.Items.Count == 3 && gi.Items[1]!.ToString() == "상급 통나무+", "gathering dropdown uses actual CLI catalog including plus name");
            gi.SelectedIndex = 2; Pump();
            var gs = All(gathering).OfType<Button>().Single(x => x.AccessibleName == "자동 채집 시작");
            Check(!gs.Enabled, "ToolOk false disables start");
            gi.SelectedIndex = 1; gq.Value = 5;
            Menu("자동 가공"); Exclusive(true);
            var ai = All(altering).OfType<ComboBox>().Single(x => x.AccessibleName == "가공 제법");
            var aq = All(altering).OfType<NumericUpDown>().Single();
            Check(ai.Items.Count == 3, "recipe dropdown exposes first CLI row for duplicate display names");
            ai.SelectedIndex = 2; Check(!All(altering).OfType<Button>().Single(x => x.AccessibleName == "자동 가공 시작").Enabled, "unavailable recipe disables start");
            ai.SelectedIndex = 1; aq.Value = 5;
            All(altering).OfType<ComboBox>().Single(x => x.AccessibleName == "가공 시설").SelectedIndex = 1;
            Menu("자동 채집"); Exclusive(false);
            Check(gi.SelectedIndex == 1 && gq.Value == 5, "switching preserves each page's independent settings");
            gs.PerformClick();
            PumpUntil(() => fake.Actions.Any(x => x.Command == "execute_gathering"));
            Check(fake.Actions.Last(x => x.Command == "execute_gathering").Name == "상급 통나무+" && Field<int>(form, "_productionTargetQuantity") == 5, "main selection and numeric quantity reach StartGatheringAsync");
            PumpUntil(() => fake.Actions.Any(x => x.Command == "stop_action"));
            PumpUntil(() => Field<string?>(form, "_activeMode") is null);
            Check(Application.OpenForms.Count == 1, "gathering starts without a settings dialog");
            Menu("자동 가공");
            All(altering).OfType<Button>().Single(x => x.AccessibleName == "자동 가공 시작").PerformClick();
            PumpUntil(() => fake.Actions.Count(x => x.Command == "execute_altering") == 2);
            PumpUntil(() => Field<string?>(form, "_activeMode") is null);
            Check(fake.Actions.Where(x => x.Command == "execute_altering").All(x => x.Name == "목재+") && Field<int>(form, "_productionTargetQuantity") == 5, "main recipe and numeric quantity reach StartAlteringAsync");
            Check(fake.Actions.Count(x => x.Command == "complete_altering_work") == 2, "existing CLI collection preserved");
            Check(Field<long>(form, "_productionCurrentQuantity") >= 5, "completion quantity is shown");
            Check(Application.OpenForms.Count == 1, "altering starts without a settings dialog");
            Menu("자동 채집"); fake.Gain = false; gq.Value = 100;
            gs.PerformClick(); PumpUntil(() => Field<string?>(form, "_activeMode") == "채집");
            Check(!gi.Enabled && !gq.Enabled, "run locks settings");
            int stops = fake.Actions.Count(x => x.Command == "stop_action");
            All(gathering).OfType<Button>().Single(x => x.AccessibleName == "정지").PerformClick();
            PumpUntil(() => fake.Actions.Count(x => x.Command == "stop_action") > stops);
            PumpUntil(() => Field<string?>(form, "_activeMode") is null);
            PumpUntil(() => gi.Enabled && gq.Enabled);
            Check(gi.Enabled && gq.Enabled, "stop uses existing cancellation and unlocks settings");
            foreach (string mode in new[] { "낚시", "던전", "어비스" })
            { Menu(mode); Check(!altering.Visible && !gathering.Visible, "legacy page preserved: " + mode); }
            fake.Fail = true; Menu("자동 채집");
            All(gathering).OfType<Button>().Single(x => x.Text == "목록 새로고침").PerformClick();
            PumpUntil(() => gi.Items.Count == 0);
            Check(!gs.Enabled && All(gathering).OfType<Label>().Any(x => x.Text.Contains("조회 실패")), "CLI failure does not fabricate catalog or allow start");
            fake.Fail = false;
            All(gathering).OfType<Button>().Single(x => x.Text == "목록 새로고침").PerformClick(); PumpUntil(() => gi.Items.Count == 3);
            Check(gs.Enabled, "catalog can recover after reconnect");
            foreach (var size in new[] { new Size(940, 700), new Size(1200, 900) })
            {
                form.ClientSize = size; Pump();
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
                        (x.Text is "진행 수량" or "진행률" || x.Text.Contains(" / ") || x.Text.EndsWith("%") || x.Text.Contains("\n"))).ToArray();
                    foreach (var value in values.Where(x => x.Height < x.Font.Height * x.Text.Split('\n').Length)) Console.WriteLine($"CLIPPED {value.Text} height={value.Height} font={value.Font.Height}");
                    Check(values.Length >= 6 && values.All(x => x.Height >= x.Font.Height * x.Text.Split('\n').Length),
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
                Menu("자동 가공"); ai.SelectedIndex = 0; aq.Value = 50;
                All(altering).OfType<ComboBox>().Single(x => x.AccessibleName == "가공 시설").SelectedIndex = 0;
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
    private bool _gathering;
    private long _logs = 38, _ingots = 12, _wood = 20;
    private string? _work;
    public Task<CliProcessOutput> Query(string command, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(Fail) return Task.FromResult(new CliProcessOutput(5,"{\"pipe\":\"disconnected\"}",""));
        if(command=="get_items" && _gathering && Gain) _logs+=3;
        object data = command switch
        {
            "status" => new {pipe="connected"},
            "capabilities" => new { commands = new[]{"get_my_info","get_currencies","get_gatherable_items","get_activity","get_inventory","get_items","execute_gathering","stop_action","get_alterable_items","get_altering_works","execute_altering","complete_altering_work"}.Select(x=>new{Command=x,Metadata=new{requiresConfirm=true}}).ToArray() },
            "get_my_info" => new {CharacterId="ui-test",CharacterName="테스트",RealmName="테스트 서버"},
            "get_currencies" => new[]{new{DisplayName="골드",Amount=5000}},
            "get_gatherable_items" => new {items=new[]{new{DisplayName="철 광석",ToolOk=true},new{DisplayName="상급 통나무+",ToolOk=true},new{DisplayName="가죽",ToolOk=false}}},
            "get_alterable_items" => new {items=new[]{new{DisplayName="철괴",Alterable=true,ProducedPerWork=3,Reason=(string?)null},new{DisplayName="목재+",Alterable=true,ProducedPerWork=3,Reason=(string?)null},new{DisplayName="철괴",Alterable=false,ProducedPerWork=7,Reason=(string?)"not_enough_ingredient"},new{DisplayName="강철괴",Alterable=false,ProducedPerWork=3,Reason=(string?)"not_enough_ingredient"}}},
            "get_items" => new[]{new{DisplayName="철 광석",Count=184L,Location="inventory"},new{DisplayName="상급 통나무+",Count=_logs,Location="inventory"},new{DisplayName="철괴",Count=_ingots,Location="inventory"},new{DisplayName="목재+",Count=_wood,Location="inventory"}},
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
