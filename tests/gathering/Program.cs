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

int activityRejects = 0, itemRejects = 0, weightRejects = 0;
var loadingCli = new MabinogiMobileCli(log, true, (command, ct) =>
{
    ct.ThrowIfCancellationRequested();
    if (command == "get_activity")
    {
        activityRejects++;
        if (activityRejects <= 2)
            return Task.FromResult(new CliProcessOutput(0, "{\"status\":\"rejected\",\"reason\":\"loading\"}", ""));
        return Task.FromResult(new CliProcessOutput(0,
            "{\"IsDead\":false,\"IsReviving\":false,\"IsInCombat\":false,\"IsAutoPlaying\":false,\"IsAutoTraveling\":false,\"IsDialoguePlaying\":false,\"IsWaitingForSelection\":false,\"Dungeon\":{\"State\":\"NotInDungeon\"},\"Battlefield\":{\"IsInBattleField\":false},\"Tutorial\":{\"IsPlaying\":false},\"Scenario\":{\"IsInScenario\":false},\"Performance\":{\"IsPlaying\":false},\"Mode\":{\"IsPlayingMiniGame\":false,\"IsHousingEditMode\":false,\"MainButtonState\":\"Compass\"},\"Interaction\":{\"HasTarget\":false,\"AvailableInteractionType\":\"None\",\"LastRunningInteractionType\":\"None\"}}", ""));
    }
    if (command == "get_items")
    {
        itemRejects++;
        if (itemRejects <= 2)
            return Task.FromResult(new CliProcessOutput(0, "{\"status\":\"rejected\",\"reason\":\"loading\"}", ""));
        return Task.FromResult(new CliProcessOutput(0,
            "[{\"DisplayName\":\"철 광석\",\"Count\":42,\"Location\":\"inventory\"}]", ""));
    }
    if (command == "get_inventory")
    {
        weightRejects++;
        if (weightRejects <= 2)
            return Task.FromResult(new CliProcessOutput(0, "{\"status\":\"rejected\",\"reason\":\"loading\"}", ""));
        return Task.FromResult(new CliProcessOutput(0,
            "{\"CurrentInventoryWeightAsDecimal\":1,\"MaxInventoryWeightAsDecimal\":100}", ""));
    }
    throw new Exception("unexpected loading retry command: " + command);
});
var loadingData = new GatheringCliData(loadingCli);
Check((await loadingData.ActivityAsync(default)).IsSafeField && activityRejects == 3,
    "gathering retries transient get_activity loading rejection");
Check(await loadingData.ItemCountAsync("철 광석", default) == 42 && itemRejects == 3,
    "gathering retries transient get_items loading rejection");
Check((await loadingData.WeightAsync(default)).Maximum == 100 && weightRejects == 3,
    "gathering retries transient get_inventory loading rejection");
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
Check(LifeSkillProfilePolicy.IsConfirmed(true, false, 0, 0),
    "profile opens from stable stat OCR without visual fallback");
Check(LifeSkillProfilePolicy.IsConfirmed(false, true, 0.24, 0.02),
    "profile accepts bottom life-skill label plus large body transition when stat OCR misses");
Check(LifeSkillProfilePolicy.IsConfirmed(false, false, 0.36, 0.12),
    "profile accepts strong fixed-screen transition when all profile OCR misses");
Check(!LifeSkillProfilePolicy.MayRetryToggle(0.20, 0.10),
    "C toggle is never retried after a meaningful profile-like screen transition");
Check(LifeSkillProfilePolicy.MayRetryToggle(0.01, 0.01),
    "C may retry only when both profile regions remain effectively unchanged");
Check(LifeSkillStopPolicy.IsStoppedAfterSpace(false, false, false),
    "post-Space completion accepts all real gathering activities stopped");
var staleStopButton = new GatheringActivity(
    false, false, false, false, false, false, false,
    "NotInDungeon", false, false, false, false, false, false,
    "Stop", false, "None", "None");
Check(LifeSkillStopPolicy.IsStoppedAfterSpace(staleStopButton),
    "post-Space completion ignores stale MainButtonState Stop when all real activities ended");
Check(!LifeSkillStopPolicy.IsStoppedAfterSpace(true, false, false),
    "post-Space completion rejects active gathering");
Check(!LifeSkillStopPolicy.IsStoppedAfterSpace(false, true, false),
    "post-Space completion rejects active travel");
Check(!LifeSkillStopPolicy.IsStoppedAfterSpace(false, false, true),
    "post-Space completion rejects active fishing");
long bulkOwned = 0;
int bulkStarts = 0;
int bulkWaits = 0;
long seenTargetTotal = -1;
var bulk = new LifeSkillBulkGatheringAutomation(
    _ => Task.FromResult(bulkOwned),
    _ =>
    {
        bulkStarts++;
        return Task.CompletedTask;
    },
    (before, targetTotal, _) =>
    {
        bulkWaits++;
        seenTargetTotal = targetTotal;
        bulkOwned = targetTotal + 110;
        return Task.FromResult(true);
    });
await bulk.RunAsync(new GatheringPlan("통나무", 340), default);
Check(bulkStarts == 1 && bulkWaits == 1 && seenTargetTotal == 340 && bulkOwned == 450,
    "life-skill bulk gathering stops the active 100-action cycle when requested material target is reached");

var plan=new GatheringPlan("철 광석",5);
try { (plan with {SourceRecipe=new AlteringPlan("금속 가공 시설","철괴(철 광석)",1,3,true)}).Validate(); throw new Exception("paid source accepted"); }
catch(InvalidDataException){Check(true,"source recipe cannot enable paid altering button");}
var success=new FakeWorld();
await new GatheringAutomation(success,success,(_,_)=>Task.CompletedTask).RunAsync(plan,default);
Check(success.Starts==1 && success.Stops==1 && success.Owned==106,"target counts new inventory only and stops after target");

var silentComplete=new FakeWorld{SilentCompleteOnStart=true};
await new GatheringAutomation(silentComplete,silentComplete,(_,_)=>Task.CompletedTask).RunAsync(plan,default);
Check(silentComplete.Starts==1 && silentComplete.Owned==105 && !silentComplete.State.IsGathering && !silentComplete.State.IsAutoTraveling,
    "inventory target completion succeeds even when activity never exposes gathering/travel");

var autoPlayingField = new FakeWorld { State = FakeWorld.Idle with { IsAutoPlaying = true } };
await new GatheringAutomation(autoPlayingField,autoPlayingField,(_,_)=>Task.CompletedTask).RunAsync(plan,default);
Check(autoPlayingField.Starts==1 && autoPlayingField.Owned>=105,
    "IsAutoPlaying alone is allowed in safe field gathering state");

var battlefieldFlagField = new FakeWorld { State = FakeWorld.Idle with { IsInBattlefield = true } };
await new GatheringAutomation(battlefieldFlagField,battlefieldFlagField,(_,_)=>Task.CompletedTask).RunAsync(plan,default);
Check(battlefieldFlagField.Starts==1 && battlefieldFlagField.Owned>=105,
    "Battlefield flag alone is allowed when all real hazard flags are clear");
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
    internal bool Tool=true,Full,IgnoreStop,TravelOnStart,SilentCompleteOnStart;
    public Task<IReadOnlyList<GatherableItem>> CatalogAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult<IReadOnlyList<GatherableItem>>(new[]{new GatherableItem("철 광석",Tool)});}
    public Task<GatheringActivity> ActivityAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(State);}
    public Task<(decimal Current,decimal Maximum)> WeightAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(((decimal)(Full?100:1),100m));}
    public Task<long> ItemCountAsync(string name,CancellationToken ct){ct.ThrowIfCancellationRequested();if(State.IsGathering)Owned+=Gain;return Task.FromResult(Owned);}
    public Task StartAsync(GatheringPlan plan,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Starts++;
        if(SilentCompleteOnStart)
        {
            Owned+=plan.TargetQuantity;
            State=Idle;
        }
        else
            State=Idle with{MainButtonState="Stop",LastRunningInteractionType="Gathering",IsAutoTraveling=TravelOnStart};
        return Task.CompletedTask;
    }
    public Task StopAsync(CancellationToken ct){ct.ThrowIfCancellationRequested();Stops++;if(!IgnoreStop)State=Idle;return Task.CompletedTask;}
    public void Dispose(){}
}
