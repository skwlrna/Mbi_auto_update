using FishingAutomation;

try
{
int checks=0;
void Check(bool ok,string label) { if(!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
var log = new AppLog(Path.Combine(AppContext.BaseDirectory,"gathering-test.log"), 1000000);
int launches=0;
foreach(bool zeroWing in new[]{true,false})
{
    var cli=new MabinogiMobileCli(log,zeroWing,(_,_) => { launches++; return Task.FromResult(new CliProcessOutput(0,"{}","")); });
    foreach(string command in new[]{"execute_gathering","execute_altering","execute_crafting","stop_action","get_gatherable_items extra","get_inventory;execute_gathering"})
        Check((await cli.QueryAsync(command)).State=="blocked","paid and action commands cannot launch: " + command);
}
Check(launches==0,"no action runner launched regardless of ZeroWingMode");
var response=MabinogiMobileCli.Parse("get_gatherable_items",new(0,"{\"items\":[{\"DisplayName\":\"상급 통나무+\",\"ToolOk\":true},{\"DisplayName\":\"철 광석\",\"ToolOk\":false}]}",""));
var catalog=GatheringQueries.ParseCatalog(response);
Check(catalog[0].DisplayName=="상급 통나무+" && !catalog[1].ToolOk,"catalog preserves exact plus names and unavailable tools");
try { GatheringQueries.ParseCatalog(MabinogiMobileCli.Parse("get_gatherable_items",new(0,"{\"items\":[{\"DisplayName\":\"철 광석\",\"ToolOk\":\"true\"}]}",""))); throw new Exception("bad tool data accepted"); }
catch(InvalidOperationException) { checks++; }
try { GatheringQueries.ParseWeight(MabinogiMobileCli.Parse("get_inventory",new(0,"{\"CurrentInventoryWeightAsDecimal\":1,\"MaxInventoryWeightAsDecimal\":0}",""))); throw new Exception("unknown capacity accepted"); }
catch(InvalidDataException) { Check(true,"unavailable bag capacity never treated as empty bag"); }
try { GatheringQueries.ParseActivity(MabinogiMobileCli.Parse("get_activity",new(0,"{}",""))); throw new Exception("missing activity accepted"); }
catch(KeyNotFoundException) { Check(true,"missing activity cannot authorize screen input"); }
Check(GatheringNavigationPolicy.IsStableFirstRow(
        new System.Drawing.Rectangle(176,620,465,66),
        new System.Drawing.Rectangle(181,624,465,66)),
    "first gathering row tolerates normal OCR/header jitter");
Check(!GatheringNavigationPolicy.IsStableFirstRow(
        new System.Drawing.Rectangle(176,620,465,66),
        new System.Drawing.Rectangle(176,700,465,66)),
    "first gathering row rejects a different vertical row");
Check(!GatheringNavigationPolicy.IsStableFirstRow(
        new System.Drawing.Rectangle(176,620,465,66),
        new System.Drawing.Rectangle(310,620,300,66)),
    "first gathering row rejects a large horizontal/layout change");
var plan=new GatheringPlan("철 광석",5);
try { (plan with {SourceRecipe=new AlteringPlan("금속 가공 시설","철괴(철 광석)",1,3,true)}).Validate(); throw new Exception("paid source accepted"); }
catch(InvalidDataException){Check(true,"source recipe cannot enable paid altering button");}
var success=new FakeWorld();
await new GatheringAutomation(success,success,(_,_)=>Task.CompletedTask).RunAsync(plan,default);
Check(success.Starts==1 && success.Stops==1 && success.Owned==106,"target counts new inventory only and stops after target");

var autoPlayingField = new FakeWorld { State = FakeWorld.Idle with { IsAutoPlaying = true } };
await new GatheringAutomation(autoPlayingField,autoPlayingField,(_,_)=>Task.CompletedTask).RunAsync(plan,default);
Check(autoPlayingField.Starts==1 && autoPlayingField.Owned>=105,
    "IsAutoPlaying alone is allowed in safe field gathering state");
foreach(var world in new[]{new FakeWorld {Tool=false},new FakeWorld{Full=true},new FakeWorld{State=FakeWorld.Idle with {IsInCombat=true}},new FakeWorld{State=FakeWorld.Idle with {IsDialoguePlaying=true}},new FakeWorld{State=FakeWorld.Idle with {IsAutoTraveling=true}}})
{
    try{await new GatheringAutomation(world,world).RunAsync(plan,default);throw new Exception("unsafe start accepted");}
    catch(InvalidOperationException){Check(world.Starts==0,"unavailable tool/full bag/unsafe state cannot start");}
}
var stalled=new FakeWorld{Gain=0};
try{await new GatheringAutomation(stalled,stalled,(_,_)=>Task.CompletedTask,3).RunAsync(plan,default);throw new Exception("stalled run accepted");}
catch(InvalidOperationException){Check(stalled.Starts==1 && stalled.Stops==1,"no progress stops without another start");}
var canceled=new FakeWorld{Gain=0};
using(var cts=new CancellationTokenSource())
{
    try{await new GatheringAutomation(canceled,canceled,(_,token)=>{cts.Cancel();token.ThrowIfCancellationRequested();return Task.CompletedTask;}).RunAsync(plan,cts.Token);throw new Exception("stop ignored");}
    catch(OperationCanceledException){Check(canceled.Stops==1,"F10-style cancellation still uses independent guarded stop token");}
}
var travel=new FakeWorld{Gain=0,TravelOnStart=true};
using(var cts=new CancellationTokenSource())
{
    try{await new GatheringAutomation(travel,travel,(_,token)=>{cts.Cancel();token.ThrowIfCancellationRequested();return Task.CompletedTask;}).RunAsync(plan,cts.Token);throw new Exception("travel stop ignored");}
    catch(OperationCanceledException){Check(travel.Stops==1 && !travel.State.IsAutoTraveling,"cancel during owned travel stops before later auto gathering");}
}
var cannotStop=new FakeWorld{IgnoreStop=true};
try{await new GatheringAutomation(cannotStop,cannotStop,(_,_)=>Task.CompletedTask).RunAsync(plan,default);throw new Exception("unconfirmed stop reported completion");}
catch(InvalidOperationException){Check(cannotStop.Stops==1,"completion requires confirmed stop");}
if(args.Contains("--live"))
{
    var cli=new MabinogiMobileCli(log);
    var data=new GatheringCliData(cli);
    var items=await data.CatalogAsync(default); Check(items.Count>0,"live free gathering catalog parsed");
    var activity=await data.ActivityAsync(default); Check(activity is not null,"live free activity parsed");
    var weight=await data.WeightAsync(default); Check(weight.Maximum>0,"live free bag weight parsed");
    Check(await data.ItemCountAsync("철 광석",default)>=0,"live free inventory quantity parsed");
}
Console.WriteLine($"PASS {checks} gathering checks");
}
catch(Exception ex){Console.Error.WriteLine(ex.Message);Environment.ExitCode=1;}

internal sealed class FakeWorld : IGatheringData, IGatheringScreen
{
    internal static GatheringActivity Idle => new(false,false,false,false,false,false,false,"NotInDungeon",false,false,false,false,false,false,"Compass",false,"None","None");
    internal GatheringActivity State=Idle;
    internal int Starts,Stops,Gain=2;
    internal long Owned=100;
    internal bool Tool=true,Full,IgnoreStop,TravelOnStart;
    public Task<IReadOnlyList<GatherableItem>> CatalogAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult<IReadOnlyList<GatherableItem>>(new[]{new GatherableItem("철 광석",Tool)});}
    public Task<GatheringActivity> ActivityAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(State);}
    public Task<(decimal Current,decimal Maximum)> WeightAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(((decimal)(Full?100:1),100m));}
    public Task<long> ItemCountAsync(string name,CancellationToken ct){ct.ThrowIfCancellationRequested();if(State.IsGathering)Owned+=Gain;return Task.FromResult(Owned);}
    public Task StartAsync(GatheringPlan plan,CancellationToken ct){ct.ThrowIfCancellationRequested();Starts++;State=Idle with{MainButtonState="Stop",LastRunningInteractionType="Gathering",IsAutoTraveling=TravelOnStart};return Task.CompletedTask;}
    public Task StopAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();Stops++;if(!IgnoreStop)State=Idle;return Task.CompletedTask;}
    public void Dispose(){}
}
