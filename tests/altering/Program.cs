using FishingAutomation;

try
{
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
Check(AlteringText.UniqueOcrAlias("강철괴", new[]{"강철괴", "합금강괴"}) == "강철과", "specific OCR alias is catalog checked");
Check(AlteringText.UniqueOcrAlias("강철괴", new[]{"강철괴", "강철과"}) is null && AlteringText.UniqueOcrAlias("목재+", new[]{"목재+"}) is null, "ambiguous and unsupported OCR aliases blocked");
var plan = new AlteringPlan("금속 가공 시설", "강철괴", 100, 3, false);
Check(plan.RequiredWorks == 34 && plan.ExpectedQuantity == 102 && plan.MaximumWings == 0, "target rounding with zero-wing invariant");
Check(AlteringText.Normalize("목재 +") != AlteringText.Normalize("목재"), "plus variants stay distinct");
Check(AlteringText.IsCardCandidate("강철과", "강철괴"), "faint card is only a candidate for detail verification");
Check(!AlteringText.IsCardCandidate("상급 목재", "상급 목재+") && !AlteringText.IsCardCandidate("철괴(광석)","철괴(철 광석)"), "candidate matching preserves recipe qualifiers");
Check(new AlteringPlan(plan.FacilityName, "철괴(철 광석)", 1, 3, false).OutputName == "철괴", "ingredient-qualified recipe maps to output item");
try { (plan with { AllowPaidButton = true }).Validate(); throw new Exception("paid altering plan accepted"); }
catch (InvalidDataException) { Check(true, "paid altering plans are rejected before execution"); }
foreach (var bad in new[] { plan with { TargetQuantity = 0 }, plan with { ProducedPerWork = 0 }, plan with { FacilityName = "none" } })
{
    try { bad.Validate(); throw new Exception("invalid plan accepted"); } catch (InvalidDataException) { checks++; }
}
async Task<(FakeWorld World, AlteringAutomation Automation)> Run(AlteringPlan p, Action<FakeWorld>? setup = null)
{
    var world = new FakeWorld(p); setup?.Invoke(world);
    var auto = new AlteringAutomation(world, world, (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }, 4);
    await auto.RunAsync(p, default);
    return (world, auto);
}
var success = await Run(plan);
Check(success.Automation.QueuedWorks == 34 && success.World.QueueCalls == 34, "one registration per required work");
Check(success.World.Owned == 102 && success.World.MaxQueue <= 7, "full queue waits and all results collected");
Check(success.Automation.ReservedWings == 0, "successful altering reserves zero Spirit Wings");
success = await Run(plan with { DisplayName = "철괴(광석)", TargetQuantity = 2 });
Check(success.World.Owned == 3, "qualified recipe work and inventory verification");
success = await Run(plan with { TargetQuantity = 4 }, w => w.Bonus = 2);
Check(success.World.Owned == 10, "critical rewards can exceed minimum target");
success = await Run(plan with { TargetQuantity = 4 }, w => w.AddCompleted(3));
Check(success.World.Owned == 9 && success.World.SecondStageCalls == 0, "existing completed work collected before baseline");
success = await Run(plan with { TargetQuantity = 4 }, w => { w.AddCompleted(3); w.TwoStageCollect = true; });
Check(success.World.Owned == 9 && success.World.SecondStageCalls > 0, "travel-first collect waits for second confirmed Space without duplicate receipt");
success = await Run(plan with { TargetQuantity = 10 }, w => { for(int i=0;i<7;i++) w.AddPending(waitingOnly:i>0); });
Check(success.World.QueueCalls == 4 && success.World.Owned == 33 && success.Automation.ReservedWings == 0, "full existing queue drained then extra target registered without counting old rewards or wings");
success = await Run(plan with { TargetQuantity = 4 }, w => { w.AddPending(); w.AddCompleted(3); w.Bonus=2; });
Check(success.World.QueueCalls == 2 && success.World.Owned == 20, "mixed completed and pending jobs with critical rewards excluded from new target");
success = await Run(plan with { TargetQuantity = 1 }, w => { w.AddPending(); w.Available=false; w.UnlockAfterExisting=true; });
Check(success.World.QueueCalls == 1 && success.World.Owned == 6, "recipe availability refreshed after existing rewards received");
var waiting = new FakeWorld(plan) { Freeze = true }; waiting.AddPending();
using(var cts = new CancellationTokenSource())
{
    var waitingAuto = new AlteringAutomation(waiting, waiting, (_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, 2);
    try { await waitingAuto.RunAsync(plan, cts.Token); throw new Exception("existing wait ignored cancellation"); }
    catch(OperationCanceledException) { Check(waiting.QueueCalls == 0 && waitingAuto.ReservedWings == 0, "stop during existing-work wait never spends currency"); }
}
var stalled = new FakeWorld(plan) { Freeze = true }; stalled.AddPending(waitingOnly:true);
try { await new AlteringAutomation(stalled, stalled, (_,_) => Task.CompletedTask, 2).RunAsync(plan, default); throw new Exception("stalled existing queue ignored"); }
catch(InvalidOperationException) { Check(stalled.QueueCalls == 0, "stalled existing queue stops without paid input"); }
var missing = new FakeWorld(plan) { Available = false };
try { await new AlteringAutomation(missing, missing).RunAsync(plan, default); throw new Exception("missing materials accepted without resolver"); }
catch (InvalidOperationException) { Check(missing.QueueCalls == 0, "missing materials without resolver stop before any registration"); }
var resolvedWorld = new FakeWorld(plan with { TargetQuantity = 3 }) { Available = false };
var resolver = new FakeResolver(resolvedWorld);
var resolvedAuto = new AlteringAutomation(resolvedWorld, resolvedWorld, (_, _) => Task.CompletedTask, 4, resolver);
await resolvedAuto.RunAsync(plan with { TargetQuantity = 3 }, default);
Check(resolver.Calls == 1 && resolvedWorld.QueueCalls == 1 && resolvedAuto.ReservedWings == 0,
    "missing materials are resolved inside the same zero-wing altering session");
var failed = new FakeWorld(plan) { Register = false };
var failedAuto = new AlteringAutomation(failed, failed, (_, _) => Task.CompletedTask, 2);
try { await failedAuto.RunAsync(plan, default); throw new Exception("unregistered click accepted"); }
catch (InvalidOperationException) { Check(failed.QueueCalls == 1 && failedAuto.ReservedWings == 0, "uncertain registration never reserves Spirit Wings"); }
var noReceipt = new FakeWorld(plan with { TargetQuantity = 1 }) { CreditRewards = false };
try { await new AlteringAutomation(noReceipt, noReceipt, (_, _) => Task.CompletedTask, 2).RunAsync(plan with { TargetQuantity = 1 }, default); throw new Exception("missing receipt accepted"); }
catch (InvalidOperationException) { Check(noReceipt.QueueCalls == 1, "completion requires inventory receipt"); }
var cancelled = new FakeWorld(plan);
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    try { await new AlteringAutomation(cancelled, cancelled).RunAsync(plan, cts.Token); throw new Exception("stop ignored"); }
    catch (OperationCanceledException) { Check(cancelled.QueueCalls == 0, "stop prevents all input"); }
}
success = await Run(plan with { TargetQuantity = 1, RecipeOrdinal = 2 }, w => w.Duplicate = true);
Check(success.World.QueueCalls == 1, "duplicate recipe variant remains selectable");
Console.WriteLine($"PASS {checks} altering workflow checks");

}
catch(Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }

internal sealed class FakeWorld : IAlteringData, IAlteringScreen
{
    private readonly AlteringPlan _plan;
    private readonly List<AlteringWork> _works = new();
    private int _polls;
    internal int QueueCalls, MaxQueue, Bonus, ExistingRemaining, SecondStageCalls;
    internal long Owned;
    internal bool Register = true, Available = true, CreditRewards = true, Duplicate, Freeze, UnlockAfterExisting, TwoStageCollect;
    internal FakeWorld(AlteringPlan plan) => _plan = plan;
    public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        bool available = Available || (UnlockAfterExisting && ExistingRemaining == 0);
        var missing = available ? Array.Empty<AlteringIngredient>() : new[] { new AlteringIngredient("철 광석", 3, 0) };
        var r = new AlteringRecipe(_plan.DisplayName, available, _plan.ProducedPerWork, available ? null : "not_enough_ingredient", missing, _plan.FacilityName);
        return Task.FromResult<IReadOnlyList<AlteringRecipe>>(Duplicate ? new[] { r with { Alterable = false }, r } : new[] { r });
    }
    public Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (++_polls % 3 == 0 && !Freeze)
        {
            int index = _works.FindIndex(x => !x.IsCompleted);
            if (index >= 0)
            {
                _works[index] = _works[index] with { State = "Completed", IsCompleted = true, RemainingSeconds = 0 };
                int next = _works.FindIndex(x => !x.IsCompleted);
                if(next >= 0) _works[next] = _works[next] with { State = "InProgress" };
            }
        }
        return Task.FromResult<IReadOnlyList<AlteringWork>>(_works.ToArray());
    }
    public Task<long> ItemCountAsync(string name, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (name != _plan.OutputName) throw new Exception("wrong output mapping"); return Task.FromResult(Owned); }
    public Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(ExistingRemaining > 0) throw new Exception("new work queued before existing jobs were drained");
        QueueCalls++;
        if (Register) _works.Add(new(plan.OutputName, plan.FacilityName, "InProgress", false, 5));
        MaxQueue = Math.Max(MaxQueue, _works.Count);
        return Task.CompletedTask;
    }
    public Task CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!TwoStageCollect) ApplyCollection(plan);
        return Task.CompletedTask;
    }
    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        SecondStageCalls++;
        if (!TwoStageCollect) return Task.FromResult(false);
        ApplyCollection(plan);
        return Task.FromResult(true);
    }
    private void ApplyCollection(AlteringPlan plan)
    {
        ExistingRemaining = Math.Max(0, ExistingRemaining - _works.Count(x => x.IsCompleted));
        if (CreditRewards) Owned += _works.Count(x => x.IsCompleted) * (plan.ProducedPerWork + Bonus);
        _works.RemoveAll(x => x.IsCompleted);
    }
    internal void AddPending(bool waitingOnly = false) { ExistingRemaining++; _works.Add(new(_plan.OutputName, _plan.FacilityName, waitingOnly ? "NotStarted" : "InProgress", false, 5)); }
    internal void AddCompleted(int count) { ExistingRemaining++; _works.Add(new(_plan.OutputName, _plan.FacilityName, "Completed", true, 0)); }
    public void Dispose() { }
}


internal sealed class FakeResolver(FakeWorld world) : IAlteringSupplyResolver
{
    internal int Calls;
    public Task ResolveAsync(AlteringPlan parentPlan, AlteringRecipe blockedRecipe, int remainingWorks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        world.Available = true;
        return Task.CompletedTask;
    }
}
