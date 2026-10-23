using FishingAutomation;

try
{
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
// V3.2.44: safe pre-input transient reads isolate only the empty lane.
var v3244Food = new AlteringPlan("식재료 가공 시설", "밥", 1, 1, false);
var v3244Metal = new AlteringPlan("금속 가공 시설", "철괴", 1, 1, false);
int v3244MetalCalls = 0, v3244FoodCalls = 0, v3244Verifications = 0,
    v3244FoodPrep = 0;
var v3244Reports = new List<MultiAlteringReadQuarantineSnapshot>();
DateTimeOffset v3244Now = new(2026, 10, 20, 0, 0, 0, TimeSpan.Zero);
var v3244Runner = new MultiAlteringCoordinator(now: () => v3244Now,
    optimizeFacilityOrder: true);
await v3244Runner.RunAsync(
    new[] { v3244Food, v3244Metal },
    (p, count, ct) =>
    {
        if (p.FacilityName == v3244Food.FacilityName)
        {
            Check(v3244Verifications == 2,
                "V3.2.44 no food registration before verified fresh read");
            v3244FoodCalls++;
        }
        else v3244MetalCalls++;
        return Task.FromResult(true);
    },
    ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
    (_, ct) =>
    {
        v3244Now += TimeSpan.FromSeconds(31);
        return Task.CompletedTask;
    }, CancellationToken.None,
    preparePlan: (p, ct) =>
    {
        if (p.FacilityName == v3244Food.FacilityName &&
            ++v3244FoodPrep == 1)
            throw new SafePreInputReadException("preflight-recipes");
        return Task.CompletedTask;
    },
    verifySafePreInputRead: (p, ct) =>
    {
        v3244Verifications++;
        if (v3244Verifications == 1)
            throw new SafePreInputReadException("preflight-recipes");
        // Even after read success, the normal preparation and batch guards
        // must be executed again; a read is not an input authorization.
        return Task.CompletedTask;
    },
    reportReadQuarantine: record => v3244Reports.Add(record));
Check(v3244MetalCalls == 1 && v3244FoodCalls == 1 &&
      v3244FoodPrep == 2 && v3244Verifications == 2 &&
      v3244Reports.Count == 2 && v3244Reports[0].Repeated &&
      v3244Reports[0].ConsecutiveFailures == 2 &&
      !v3244Reports[0].VerifiedRecovered &&
      v3244Reports[1].VerifiedRecovered &&
      v3244Runner.ReadQuarantine.Snapshot(new[] { v3244Food, v3244Metal })
        .Single().VerifiedRecovered &&
      v3244Runner.Efficiency.Snapshot().Single(x =>
        x.Facility == v3244Food.FacilityName).HeldPasses >= 1,
    "V3.2.44 repeated safe read failure isolated, healthy lane ran, fresh read recovered");

var v3244Dir = Path.Combine(Path.GetTempPath(),
    "mabiauto-v3244-" + Guid.NewGuid().ToString("N"));
try
{
    var diagnostic = MultiAlteringReadQuarantineAudit.Write(
        v3244Dir, v3244Reports[0]);
    var evidenceText = File.ReadAllText(diagnostic);
    Check(evidenceText.Contains("safe_pre_input_read_quarantine", StringComparison.Ordinal) &&
          evidenceText.Contains("autoFixReviewOnly", StringComparison.Ordinal) &&
          evidenceText.Contains("inputPermittedByThisEvidence", StringComparison.Ordinal) &&
          !evidenceText.Contains("CharacterId", StringComparison.Ordinal) &&
          !evidenceText.Contains("AccountCode", StringComparison.Ordinal) &&
          !evidenceText.Contains("exceptionMessage", StringComparison.Ordinal),
        "V3.2.44 sanitized bounded incident is AI review only, never game authority");
}
finally { if (Directory.Exists(v3244Dir)) Directory.Delete(v3244Dir, true); }

bool v3244StateErrorStopped = false;
int v3244StateInputCalls = 0;
try
{
    await new MultiAlteringCoordinator().RunAsync(
        new[] { v3244Food },
        (p, n, ct) =>
        {
            v3244StateInputCalls++;
            return Task.FromResult(true);
        },
        ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
        (_, ct) => Task.CompletedTask,
        CancellationToken.None,
        preparePlan: (p, ct) => throw new InvalidOperationException("unverified state"),
        verifySafePreInputRead: (p, ct) => Task.CompletedTask);
}
catch (InvalidOperationException) { v3244StateErrorStopped = true; }
Check(v3244StateErrorStopped && v3244StateInputCalls == 0,
    "V3.2.44 unknown state conflict still halts whole coordinator without game input");

bool v3244ExistingWorkStopped = false;
try
{
    await new MultiAlteringCoordinator().RunAsync(
        new[] { v3244Food },
        (p, n, ct) => Task.FromResult(true),
        ct => Task.FromResult<IReadOnlyList<AlteringWork>>(new[]
        {
            new AlteringWork("밥", v3244Food.FacilityName, "Completed", true, 0)
        }),
        (_, ct) => Task.CompletedTask, CancellationToken.None,
        preparePlan: (p, ct) => throw new SafePreInputReadException("preflight-works"),
        verifySafePreInputRead: (p, ct) => Task.CompletedTask);
}
catch (SafePreInputReadException) { v3244ExistingWorkStopped = true; }
Check(v3244ExistingWorkStopped,
    "V3.2.44 even typed read failure does not isolate occupied or already-completed lane");

bool v3244PostInputStopped = false;
try
{
    await new MultiAlteringCoordinator().RunAsync(
        new[] { v3244Food },
        (p, n, ct) => throw new InvalidOperationException("unconfirmed registration"),
        ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
        (_, ct) => Task.CompletedTask, CancellationToken.None,
        verifySafePreInputRead: (p, ct) => Task.CompletedTask);
}
catch (InvalidOperationException) { v3244PostInputStopped = true; }
Check(v3244PostInputStopped,
    "V3.2.44 post-input errors are never converted to recoverable read quarantine");

bool v3244Cancelled = false;
using (var v3244Cts = new CancellationTokenSource())
{
    v3244Cts.Cancel();
    try
    {
        await new MultiAlteringCoordinator().RunAsync(
            new[] { v3244Food }, (p, n, ct) => Task.FromResult(true),
            ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
            (_, ct) => Task.CompletedTask, v3244Cts.Token,
            verifySafePreInputRead: (p, ct) => Task.CompletedTask);
    }
    catch (OperationCanceledException) { v3244Cancelled = true; }
}
Check(v3244Cancelled, "V3.2.44 F10 cancellation is not swallowed by lane isolation");

// V3.2.43: read-only facility ordering prefers a fully completed batch
// over a new empty lane. A held lane never becomes input permission.
var v3243Tracker = new MultiAlteringFacilityEfficiencyTracker();
var v3243Metal = "금속 가공 시설";
var v3243Wood = "목재 가공 시설";
var v3243Food = "식재료 가공 시설";
var v3243CompletedWood = new[]
{
    new AlteringWork("목재", v3243Wood, "Completed", true, 0)
};
v3243Tracker.Observe(v3243Metal, Array.Empty<AlteringWork>(), false, false);
v3243Tracker.Observe(v3243Wood, v3243CompletedWood, false, false);
v3243Tracker.Observe(v3243Food, Array.Empty<AlteringWork>(), true, false);
var v3243InputOrder = new[] { v3243Metal, v3243Wood, v3243Food };
Check(v3243Tracker.Order(v3243InputOrder, false).SequenceEqual(v3243InputOrder),
    "V3.2.43 existing callers default to original facility order");
Check(v3243Tracker.Order(v3243InputOrder, true).SequenceEqual(
    new[] { v3243Wood, v3243Metal, v3243Food }),
    "V3.2.43 completed batch prioritized, held lane remains last");
v3243Tracker.NoteBatchAttempt(v3243Wood);
v3243Tracker.NoteVerifiedProgress(v3243Wood, true);
v3243Tracker.EndPass();
var v3243Metrics = v3243Tracker.Snapshot();
Check(v3243Metrics.Single(x => x.Facility == v3243Wood).BatchCalls == 1 &&
      v3243Metrics.Single(x => x.Facility == v3243Wood).VerifiedProgressTurns == 1 &&
      v3243Metrics.Single(x => x.Facility == v3243Wood).CompletedPlanTurns == 1 &&
      v3243Metrics.Single(x => x.Facility == v3243Food).HeldPasses == 1 &&
      v3243Metrics.Single(x => x.Facility == v3243Metal).UnservedReadyPasses == 1,
    "V3.2.43 facility metrics separate read-only pass, held lane and verified completion");

var v3243Fabric = "옷감 가공 시설";
var v3243Fair = new MultiAlteringFacilityEfficiencyTracker();
v3243Fair.Observe(v3243Metal, Array.Empty<AlteringWork>(), false, false);
v3243Fair.Observe(v3243Fabric, new[]
{
    new AlteringWork("실", v3243Fabric, "InProgress", false, 60)
}, false, false);
v3243Fair.EndPass();
v3243Fair.Observe(v3243Metal, Array.Empty<AlteringWork>(), false, false);
v3243Fair.Observe(v3243Fabric, Array.Empty<AlteringWork>(), false, false);
Check(v3243Fair.Order(new[] { v3243Fabric, v3243Metal }, true)
        .SequenceEqual(new[] { v3243Metal, v3243Fabric }),
    "V3.2.43 repeatedly unserved ready lane wins tie without changing recipe order");

var v3243First = new AlteringPlan(v3243Metal, "철괴", 1, 1, false);
var v3243Second = new AlteringPlan(v3243Wood, "목재", 1, 1, false);
var v3243WoodNext = new AlteringPlan(v3243Wood, "목재+", 1, 1, false);
var v3243Calls = new List<string>();
var v3243Scheduler = new MultiAlteringCoordinator(optimizeFacilityOrder: true);
await v3243Scheduler.RunAsync(
    new[] { v3243First, v3243Second, v3243WoodNext },
    (p, slot, ct) =>
    {
        Check(slot == 1, "V3.2.43 retains original one-slot guarded batch budget");
        v3243Calls.Add(p.DisplayName);
        return Task.FromResult(true);
    },
    ct => Task.FromResult<IReadOnlyList<AlteringWork>>(v3243CompletedWood),
    (time, ct) => Task.CompletedTask,
    CancellationToken.None);
Check(v3243Calls.SequenceEqual(new[] { "목재", "목재+", "철괴" }) &&
      v3243Scheduler.Efficiency.Snapshot().Sum(x => x.CompletedPlanTurns) == 3,
    "V3.2.43 independent lane reorder never reorders two recipes inside the same facility");

var v3243HeldPlan = new AlteringPlan(v3243Food, "밥", 1, 1, false);
var v3243SafeMetal = new AlteringPlan(v3243Metal, "철괴", 1, 1, false);
int v3243UnsafeFoodCalls = 0, v3243IndependentMetalCalls = 0,
    v3243PersistedHolds = 0;
using (var v3243Cancel = new CancellationTokenSource())
{
    try
    {
        await new MultiAlteringCoordinator(optimizeFacilityOrder: true).RunAsync(
            new[] { v3243HeldPlan, v3243SafeMetal },
            (p, slot, ct) =>
            {
                if (p.FacilityName == v3243Food) v3243UnsafeFoodCalls++;
                else v3243IndependentMetalCalls++;
                return Task.FromResult(true);
            },
            ct => Task.FromResult<IReadOnlyList<AlteringWork>>(
                Array.Empty<AlteringWork>()),
            (_, ct) =>
            {
                v3243Cancel.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
            v3243Cancel.Token,
            preparePlan: (p, ct) =>
            {
                if (p.FacilityName == v3243Food)
                    throw new MaterialSupplyHoldException(
                        new("미확인 재료", 3, 0, "acquisition_route_unverified"));
                return Task.CompletedTask;
            },
            recheckUnavailableRoute: (p, prior, ct) =>
                Task.FromResult<MaterialSupplyHold?>(prior),
            persistMaterialHold: (p, hold) => v3243PersistedHolds++);
    }
    catch (OperationCanceledException) { }
}
Check(v3243UnsafeFoodCalls == 0 && v3243IndependentMetalCalls == 1 &&
      v3243PersistedHolds == 1,
    "V3.2.43 priority cannot bypass food hold or stall independent facility");

// V3.2.42: the report is deliberately partial and read-only, with shared
// stock allocated once across multiple selected recipes, not once per plan.
var v3242Rice = new AlteringPlan("식재료 가공 시설", "밥", 2, 1, false);
var v3242Oatmeal = new AlteringPlan("식재료 가공 시설", "오트밀", 3, 1, false);
var v3242First = new AlteringRecipe("밥", false, 1,
    "not_enough_ingredient", new[]
    {
        new AlteringIngredient("물이 든 병", 2, 999), // stale Owned must be ignored
        new AlteringIngredient("석양 나비", 1, 0),
        new AlteringIngredient("도구없는 식재료", 1, 0)
    }, v3242Rice.FacilityName);
var v3242Second = new AlteringRecipe("오트밀", false, 1,
    "not_enough_ingredient", new[]
    {
        new AlteringIngredient("물이 든 병", 1, 0),
        new AlteringIngredient("공급 경로 모름", 1, 0)
    }, v3242Oatmeal.FacilityName);
var v3242Stock = new Dictionary<string, long>(StringComparer.Ordinal)
{
    ["물이 든 병"] = 2, ["석양 나비"] = 0,
    ["도구없는 식재료"] = 0, ["공급 경로 모름"] = 0
};
var v3242Reads = new Dictionary<string, int>(StringComparer.Ordinal);
Task<long> V3242Read(string item, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    v3242Reads[item] = v3242Reads.GetValueOrDefault(item) + 1;
    return Task.FromResult(v3242Stock[item]);
}
var v3242Roots = new[]
{
    new MultiAlteringSupplyPreflight(v3242Rice, v3242First, 2),
    new MultiAlteringSupplyPreflight(v3242Oatmeal, v3242Second, 3)
};
var v3242Forecast = await MultiAlteringMaterialForecastPolicy.ForecastAsync(
    v3242Roots, new[]
    {
        new GatherableItem("물이 든 병", true),
        new GatherableItem("도구없는 식재료", false)
    }, V3242Read, CancellationToken.None);
var v3242Water = v3242Forecast.Single(x => x.DisplayName == "물이 든 병");
Check(v3242Water.EvidencedDemand == 7 && v3242Water.FreshStock == 2 &&
      v3242Water.Shortage == 5 && v3242Water.SelectedRecipes == 2 &&
      v3242Water.AcquisitionRoute == MultiAlteringMaterialForecastPolicy.SupportedBulk &&
      v3242Forecast[0].DisplayName == "물이 든 병" &&
      v3242Reads.Values.All(count => count == 1),
    "V3.2.42 shared ingredients are summed once and fresh stock is read once per item");
Check(v3242Forecast.Single(x => x.DisplayName == "석양 나비").AcquisitionRoute ==
          MultiAlteringMaterialForecastPolicy.QuestOnly &&
      v3242Forecast.Single(x => x.DisplayName == "도구없는 식재료").AcquisitionRoute ==
          MultiAlteringMaterialForecastPolicy.MissingTool &&
      v3242Forecast.Single(x => x.DisplayName == "공급 경로 모름").AcquisitionRoute ==
          MultiAlteringMaterialForecastPolicy.Unverified,
    "V3.2.42 forecast distinguishes verified bulk, quest-only, missing tool and unknown acquisition");

var v3242ReadsBefore = v3242Reads.Values.Sum();
var v3242Masked = v3242First with
{
    Reason = "queue_full", MissingIngredients = Array.Empty<AlteringIngredient>()
};
Check((await MultiAlteringMaterialForecastPolicy.ForecastAsync(
    new[] { new MultiAlteringSupplyPreflight(v3242Rice, v3242Masked, 2) },
    Array.Empty<GatherableItem>(), V3242Read, CancellationToken.None)).Count == 0 &&
    v3242Reads.Values.Sum() == v3242ReadsBefore,
    "V3.2.42 a masked or empty CLI recipe never invents a full bill of materials");

bool v3242DuplicatesRejected = false;
try
{
    await MultiAlteringMaterialForecastPolicy.ForecastAsync(
        new[] { v3242Roots[0], v3242Roots[0] }, Array.Empty<GatherableItem>(),
        V3242Read, CancellationToken.None);
}
catch (InvalidDataException) { v3242DuplicatesRejected = true; }
Check(v3242DuplicatesRejected, "V3.2.42 duplicate selected plan cannot double-charge material stock");

bool v3242InvalidStockRejected = false;
v3242Stock["물이 든 병"] = -1;
try
{
    await MultiAlteringMaterialForecastPolicy.ForecastAsync(
        v3242Roots, Array.Empty<GatherableItem>(), V3242Read, CancellationToken.None);
}
catch (InvalidDataException) { v3242InvalidStockRejected = true; }
Check(v3242InvalidStockRejected, "V3.2.42 negative live stock is never replaced with stale Owned evidence");
v3242Stock["물이 든 병"] = 2;

bool v3242OverflowRejected = false;
try
{
    await MultiAlteringMaterialForecastPolicy.ForecastAsync(
        new[] { new MultiAlteringSupplyPreflight(v3242Rice,
            v3242First with { MissingIngredients = new[]
            { new AlteringIngredient("물이 든 병", long.MaxValue, 0) } }, 2) },
        Array.Empty<GatherableItem>(), V3242Read, CancellationToken.None);
}
catch (OverflowException) { v3242OverflowRejected = true; }
Check(v3242OverflowRejected, "V3.2.42 checked forecast prevents numeric wraparound");

bool v3242Canceled = false;
using (var v3242Cancel = new CancellationTokenSource())
{
    v3242Cancel.Cancel();
    try
    {
        await MultiAlteringMaterialForecastPolicy.ForecastAsync(
            v3242Roots, Array.Empty<GatherableItem>(), V3242Read, v3242Cancel.Token);
    }
    catch (OperationCanceledException) { v3242Canceled = true; }
}
Check(v3242Canceled, "V3.2.42 F10 cancellation stops diagnostic stock reads promptly");

// V3.2.41: durable quarantine is never an authorization to register a
// previously held item until fresh live evidence removes it.
var v3241Food = new AlteringPlan("식재료 가공 시설", "밥", 2, 1, false);
var v3241Metal = new AlteringPlan("금속 가공 시설", "철괴", 2, 1, false);
var v3241Plans = new[] { v3241Food, v3241Metal };
var v3241Saved = new MultiAlteringMaterialHoldCheckpoint
{
    FacilityName = v3241Food.FacilityName,
    PlanKey = MultiAlteringBatchStore.Key(v3241Food),
    Item = "미확인 재료", Required = 3, Observed = 0,
    Category = "acquisition_route_unverified",
    FirstHeldAt = DateTimeOffset.UtcNow, LastCheckedAt = DateTimeOffset.UtcNow
};
var v3241Clock = DateTimeOffset.Parse("2026-10-20T00:00:00+00:00");
int v3241Rechecks = 0, v3241FoodInputs = 0, v3241MetalInputs = 0;
var v3241Events = new List<string>();
var v3241Coordinator = new MultiAlteringCoordinator(now: () => v3241Clock);
v3241Coordinator.Log += v3241Events.Add;
await v3241Coordinator.RunAsync(
    v3241Plans,
    (p, _, ct) =>
    {
        if (p == v3241Food)
        {
            Check(v3241Rechecks >= 2 && v3241MetalInputs == 1,
                "V3.2.41 stale restored hold cannot execute food input");
            v3241FoodInputs++;
        }
        else v3241MetalInputs++;
        return Task.FromResult(true);
    },
    ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
    (interval, ct) => { v3241Clock += interval; return Task.CompletedTask; },
    CancellationToken.None,
    recheckUnavailableRoute: (plan, prior, ct) =>
    {
        v3241Rechecks++;
        return Task.FromResult<MaterialSupplyHold?>(
            v3241Rechecks == 1 ? prior with { Observed = 1 } : null);
    },
    restoreMaterialHolds: _ => new[] { v3241Saved },
    persistMaterialHold: (plan, evidence) =>
    {
        Check(v3241FoodInputs == 0 && evidence.Observed == 1,
            "V3.2.41 hold remains isolated during first live poll");
    },
    clearMaterialHold: plan =>
    {
        Check(v3241Rechecks == 2 && v3241FoodInputs == 0,
            "V3.2.41 clearing requires new live stock check first");
    });
Check(v3241Rechecks == 2 && v3241MetalInputs == 1 && v3241FoodInputs == 1 &&
      v3241Events.Any(x => x.Contains("[보류 복원]", StringComparison.Ordinal)),
    "V3.2.41 food hold restart makes progress only after fresh verified release");

// Failing to commit the hold is fatal BEFORE any queue input is attempted.
var v3241BlockedInputs = 0;
bool v3241DiskFailureStopped = false;
try
{
    await new MultiAlteringCoordinator().RunAsync(
        new[] { v3241Food },
        (plan, _, ct) =>
        {
            v3241BlockedInputs++;
            return Task.FromResult(true);
        },
        ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
        (_, ct) => Task.CompletedTask,
        CancellationToken.None,
        preparePlan: (plan, ct) => throw new MaterialSupplyHoldException(
            new("미확인 재료", 3, 0, "acquisition_route_unverified")),
        recheckUnavailableRoute: (plan, prior, ct) =>
            Task.FromResult<MaterialSupplyHold?>(prior),
        persistMaterialHold: (plan, evidence) => throw new IOException("atomic hold failure"));
}
catch (IOException ex) when (ex.Message == "atomic hold failure")
{ v3241DiskFailureStopped = true; }
Check(v3241DiskFailureStopped && v3241BlockedInputs == 0,
    "V3.2.41 hold journal failure safely stops without registration");

// V3.2.40: unknown food inputs are held, never invented as gatherable.
var v3240Food = new AlteringPlan("식재료 가공 시설", "밥", 2, 1, false);
var v3240Recipe = new AlteringRecipe("밥", false, 1,
    "not_enough_ingredient",
    new[] { new AlteringIngredient("확보경로 미확인 재료", 2, 0) }, v3240Food.FacilityName);
var v3240Counts = new Dictionary<string, long>(StringComparer.Ordinal)
{ ["확보경로 미확인 재료"] = 0 };
Task<long> V3240Count(string item, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    return Task.FromResult(v3240Counts.GetValueOrDefault(item));
}
var v3240NoRoute = await MaterialSupplyRoutePolicy.FindHoldAsync(
    v3240Food, v3240Recipe, new[] { v3240Recipe },
    Array.Empty<GatherableItem>(), V3240Count, CancellationToken.None);
Check(v3240NoRoute?.Category == "acquisition_route_unverified" &&
      v3240NoRoute.Required == 4 && v3240NoRoute.Observed == 0,
    "V3.2.40 an item absent from gather and recipe listings is a hold, not a proven impossibility");
v3240Counts["확보경로 미확인 재료"] = 4;
Check(await MaterialSupplyRoutePolicy.FindHoldAsync(
    v3240Food, v3240Recipe, new[] { v3240Recipe },
    Array.Empty<GatherableItem>(), V3240Count, CancellationToken.None) is null,
    "V3.2.40 fresh inventory sufficient for selected food works clears an unknown-route shortage");
v3240Counts["확보경로 미확인 재료"] = 0;
var v3240UnsupportedBulk = await MaterialSupplyRoutePolicy.FindHoldAsync(
    v3240Food, v3240Recipe, new[] { v3240Recipe },
    new[] { new GatherableItem("확보경로 미확인 재료", true) },
    V3240Count, CancellationToken.None);
Check(v3240UnsupportedBulk?.Category == "bulk_route_unverified",
    "V3.2.40 a CLI gatherable not supported by bulk screen remains safely held");
var v3240KnownGather = v3240Recipe with
{
    MissingIngredients = new[] { new AlteringIngredient("물이 든 병", 2, 0) }
};
Check(await MaterialSupplyRoutePolicy.FindHoldAsync(
    v3240Food, v3240KnownGather, new[] { v3240KnownGather },
    new[] { new GatherableItem("물이 든 병", true) },
    V3240Count, CancellationToken.None) is null,
    "V3.2.40 known bulk-supported well water keeps existing guarded gathering flow");
var v3240NoTool = await MaterialSupplyRoutePolicy.FindHoldAsync(
    v3240Food, v3240Recipe, new[] { v3240Recipe },
    new[] { new GatherableItem("확보경로 미확인 재료", false) },
    V3240Count, CancellationToken.None);
Check(v3240NoTool?.Category == "gathering_tool_unavailable",
    "V3.2.40 missing tool blocks the food lane instead of guessing another route");
var v3240Insect = v3240Recipe with {
    MissingIngredients = new[] { new AlteringIngredient("석양 나비", 1, 0) } };
var v3240QuestHold = await MaterialSupplyRoutePolicy.FindHoldAsync(
    v3240Food, v3240Insect, new[] { v3240Insect },
    Array.Empty<GatherableItem>(), V3240Count, CancellationToken.None);
Check(v3240QuestHold?.Category == "quest_only_not_bulk_gatherable",
    "V3.2.40 known quest-only insects must not start standalone F9 bulk gathering");

var v3240Metal = new AlteringPlan("금속 가공 시설", "철괴", 1, 1, false);
var v3240Cheese = new AlteringPlan("식재료 가공 시설", "치즈", 1, 1, false);
var v3240Now = DateTimeOffset.Parse("2026-10-20T00:00:00+00:00");
int v3240FoodActions = 0, v3240CheeseActions = 0, v3240MetalActions = 0,
    v3240PrepAttempts = 0, v3240FreshRechecks = 0;
long v3240Available = 0;
var v3240Logs = new List<string>();
var v3240Scheduler = new MultiAlteringCoordinator(now: () => v3240Now);
v3240Scheduler.Log += v3240Logs.Add;
await v3240Scheduler.RunAsync(
    new[] { v3240Food, v3240Cheese, v3240Metal },
    (p, _, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        if (p.FacilityName == v3240Metal.FacilityName)
        {
            v3240MetalActions++;
            Check(v3240FoodActions == 0 && v3240CheeseActions == 0,
                "V3.2.40 independent metal progresses while food and following same-lane recipe are held");
            v3240Available = 4;
            v3240Now += TimeSpan.FromSeconds(31);
        }
        else if (p.DisplayName == "밥") v3240FoodActions++;
        else v3240CheeseActions++;
        return Task.FromResult(true);
    },
    ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
    (interval, ct) => { v3240Now += interval; return Task.CompletedTask; },
    CancellationToken.None,
    preparePlan: (plan, ct) =>
    {
        if (plan.DisplayName == "밥" && ++v3240PrepAttempts == 1)
            throw new MaterialSupplyHoldException(
                new("확보경로 미확인 재료", 4, 0, "acquisition_route_unverified"));
        return Task.CompletedTask;
    },
    recheckUnavailableRoute: (plan, previous, ct) =>
    {
        v3240FreshRechecks++;
        return Task.FromResult<MaterialSupplyHold?>(
            v3240Available < previous.Required
                ? previous with { Observed = v3240Available } : null);
    });
Check(v3240MetalActions == 1 && v3240FoodActions == 1 &&
      v3240CheeseActions == 1 && v3240FreshRechecks >= 1 &&
      v3240Logs.Any(x => x.Contains("[재료 확보 대기]", StringComparison.Ordinal)) &&
      v3240Logs.Any(x => x.Contains("[재료 재확인]", StringComparison.Ordinal)),
    "V3.2.40 quarantined food resumes after material verification and preserves same-lane recipe order");

// F10 during unresolved material hold must remain responsive and never click.
var v3240Cancel = new CancellationTokenSource();
var v3240Stopped = false;
var v3240Queued = 0;
var v3240WaitCycles = 0;
var v3240Clock = v3240Now;
try
{
    await new MultiAlteringCoordinator(now: () => v3240Clock).RunAsync(
        new[] { v3240Food },
        (p, n, ct) => { v3240Queued++; return Task.FromResult(true); },
        ct => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
        (span, ct) =>
        {
            v3240Clock += span;
            if (++v3240WaitCycles >= 3) v3240Cancel.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, v3240Cancel.Token,
        preparePlan: (p, ct) => throw new MaterialSupplyHoldException(
            new("확보경로 미확인 재료", 4, 0, "acquisition_route_unverified")),
        recheckUnavailableRoute: (p, prior, ct) =>
            Task.FromResult<MaterialSupplyHold?>(prior));
}
catch (OperationCanceledException) when (v3240Cancel.IsCancellationRequested)
{ v3240Stopped = true; }
Check(v3240Stopped && v3240Queued == 0,
    "V3.2.40 F10 cancels a held food lane without replaying an unknown action");

// V3.2.36: deterministic rule-based middle-manager recovery decisions.
Check(LifeSkillRecoveryDecisionPolicy.ForReadFailure(
        new TimeoutException(), 0).Disposition ==
        LifeSkillRecoveryDisposition.RetryReadOnlyOnce &&
      LifeSkillRecoveryDecisionPolicy.ForReadFailure(
        new TimeoutException(), 1).Disposition ==
        LifeSkillRecoveryDisposition.SafeStopAndDiagnose,
    "V3.2.36 limits transient read-only recovery to one retry");
Check(LifeSkillRecoveryDecisionPolicy.ForReadFailure(
        new InvalidDataException("secret"), 0).Category == "invalid_cli_data" &&
      LifeSkillRecoveryDecisionPolicy.ForReadFailure(
        new InvalidDataException("secret"), 0).Disposition ==
        LifeSkillRecoveryDisposition.SafeStopAndDiagnose,
    "V3.2.36 semantic invalid inventory never retries");
Check(LifeSkillRecoveryDecisionPolicy.ForReadFailure(
        new TimeoutException(), 0, cancellationRequested: true).Disposition ==
        LifeSkillRecoveryDisposition.Cancel &&
      LifeSkillRecoveryDecisionPolicy.ForReadFailure(
        new OperationCanceledException(), 0).Disposition ==
        LifeSkillRecoveryDisposition.Cancel,
    "V3.2.36 cancellation always outranks a transient transport error");
Check(LifeSkillRecoveryDecisionPolicy.ForIncident(
        new TimeoutException()).Disposition ==
        LifeSkillRecoveryDisposition.SafeStopAndDiagnose &&
      LifeSkillRecoveryDecisionPolicy.ForUncertainAction().Category ==
        "unconfirmed_action",
    "V3.2.36 incident history cannot authorize GUI replay");
var v3236Journal = new LifeSkillRecoveryJournal();
for (int i = 0; i < 3; i++)
    v3236Journal.Note(LifeSkillStage.Gathering, "inventory",
        LifeSkillRecoveryDecisionPolicy.ForReadFailure(new IOException(), 0));
Check(v3236Journal.Snapshot().Single().Occurrences == 3 &&
      v3236Journal.Snapshot().Single().Disposition ==
        LifeSkillRecoveryDisposition.RetryReadOnlyOnce,
    "V3.2.36 repeated read patterns are counted, not replayed");
v3236Journal.Note(LifeSkillStage.Crafting,
    "UserAccount-raw-exception-with-sensitive-text-too-long",
    LifeSkillRecoveryDecisionPolicy.ForUncertainAction());
Check(v3236Journal.Snapshot().Last().Operation == "other",
    "V3.2.36 arbitrary raw operation strings are not kept in diagnostic patterns");
for (int i = 0; i < 30; i++)
    v3236Journal.Note(LifeSkillStage.Altering, "op" + i,
        LifeSkillRecoveryDecisionPolicy.ForIncident(new InvalidOperationException()));
Check(v3236Journal.Snapshot().Count == 24 &&
      v3236Journal.Snapshot().All(x => x.Occurrences >= 1),
    "V3.2.36 recovery history remains bounded to 24 in-memory patterns");

var v3236State = new LifeSkillManagerState(
    recoveryDelay: (_, _) => Task.CompletedTask);
v3236State.BeginStage(LifeSkillStage.Crafting, "V3.2.36 regression");
int v3236Calls = 0;
_ = await v3236State.ReadWithRecoveryAsync(
    LifeSkillStage.Crafting, "inventory",
    _ =>
    {
        if (++v3236Calls == 1) throw new IOException("sample");
        return Task.FromResult(5L);
    }, CancellationToken.None);
v3236State.RecordUncertainAction(LifeSkillStage.Crafting, "craft");
v3236State.RecordIncident(LifeSkillStage.Crafting, new InvalidOperationException());
var v3236History = v3236State.RecoveryPatternsSnapshot();
Check(v3236Calls == 2 &&
      v3236History.Any(x => x.Category == "transient_read" &&
          x.Disposition == LifeSkillRecoveryDisposition.RetryReadOnlyOnce) &&
      v3236History.Any(x => x.Category == "transient_read_recovered" &&
          x.Disposition == LifeSkillRecoveryDisposition.ContinueAfterVerifiedRead) &&
      v3236History.Any(x => x.Category == "unconfirmed_action" &&
          x.Operation == "craft") &&
      v3236History.Any(x => x.Operation == "incident" &&
          x.Category == "state_conflict"),
    "V3.2.36 cross-skill manager records actual bounded recovery decisions");

// V3.2.38: guarded attempts are not automatically completed outputs.
var v3238Fence = new LifeSkillWorkFence();
var v3238Pending = v3238Fence.Begin("craft:밥");
var v3238Unknown = v3238Fence.Begin("gather:물이 든 병");
var v3238Batch = v3238Fence.Begin("alter:food/밥/1");
v3238Fence.ConfirmBatchReturn(v3238Batch, 0, false);
var v3238BatchNext = v3238Fence.Begin("alter:food/밥/1");
v3238Fence.ConfirmBatchReturn(v3238BatchNext, 1, true);
var v3238Gather = v3238Fence.Begin("gather:철 광석");
v3238Fence.ConfirmInventoryIncrease(v3238Gather, 4, 8, 4);
v3238Fence.MarkUncertain(v3238Unknown, "not_confirmed");
v3238Fence.MarkUncertain(v3238Unknown, "duplicate"); // duplicate must not double count
var v3238Rows = v3238Fence.OutcomeSnapshot();
var v3238G = v3238Rows.Single(x => x.Kind == LifeSkillWorkKind.Gathering);
var v3238A = v3238Rows.Single(x => x.Kind == LifeSkillWorkKind.AlteringBatch);
var v3238C = v3238Rows.Single(x => x.Kind == LifeSkillWorkKind.Crafting);
Check(v3238G.Started == 2 && v3238G.OutputVerified == 1 &&
      v3238G.Uncertain == 1 && v3238G.InFlight == 0 &&
      v3238G.ConcludedOutputVerificationRate == 0.5 &&
      v3238C.Started == 1 && v3238C.InFlight == 1 &&
      v3238C.ConcludedOutputVerificationRate is null,
    "V3.2.38 verified gathering, uncertain outcome and in-flight craft remain distinct");
Check(v3238A.Started == 2 && v3238A.BatchCallbackVerified == 2 &&
      v3238A.OutputVerified == 0 &&
      v3238A.ConcludedOutputVerificationRate is null,
    "V3.2.38 F9 batch return does not masquerade as confirmed output receipt");
bool v3238MismatchedProof = false;
try { v3238Fence.ConfirmBatchReturn(v3238Pending, 1, true); }
catch (InvalidOperationException) { v3238MismatchedProof = true; }
Check(v3238MismatchedProof && v3238Fence.OutcomeSnapshot()
      .Single(x => x.Kind == LifeSkillWorkKind.Crafting).InFlight == 1,
    "V3.2.38 cross-type proof cannot incorrectly complete crafting");
v3238Fence.ConfirmInventoryIncrease(v3238Pending, 10, 12, 2);
Check(v3238Fence.OutcomeSnapshot().Single(x => x.Kind == LifeSkillWorkKind.Crafting)
      .ConcludedOutputVerificationRate == 1,
    "V3.2.38 confirmed screen-plus-inventory craft contributes only after proof");

// V3.2.39: repeated facility bottlenecks are inferred from independent
// confirmed episodes, not each read-only poll or a guessed missing item.
var v3239Now = DateTimeOffset.Parse("2026-10-19T00:00:00+00:00");
var v3239Monitor = new MultiAlteringFacilityBottleneckTracker(() => v3239Now);
const string v3239FoodFacility = "식재료 가공 시설";
v3239Monitor.ObserveWorks(v3239FoodFacility, Array.Empty<AlteringWork>());
v3239Monitor.NoteBlockedRecipe(v3239FoodFacility, "queue_full");
v3239Now += TimeSpan.FromSeconds(135);
v3239Monitor.NoteBlockedRecipe(v3239FoodFacility, "queue_full");
var v3239Snapshot = v3239Monitor.Snapshot().Single();
Check(v3239Snapshot.QueueFullEpisodes == 1 &&
      v3239Snapshot.BlockedSeconds == 135 &&
      v3239Snapshot.CurrentlyBlocked &&
      !v3239Snapshot.RepeatedIssue,
    "V3.2.39 repeated queue_full polling is one facility episode, not new omission");
v3239Monitor.ObserveWorks(v3239FoodFacility,
    new[] { new AlteringWork("기존 밥", v3239FoodFacility, "Completed", true, 0) });
v3239Monitor.NoteBlockedRecipe(v3239FoodFacility, "queue_full");
Check(v3239Monitor.Snapshot().Single().QueueFullEpisodes == 1,
    "V3.2.39 old completed slot does not falsely resolve masked recipe");
v3239Now -= TimeSpan.FromMinutes(4);
Check(v3239Monitor.Snapshot().Single().BlockedSeconds == 0,
    "V3.2.39 wall clock rollback never yields negative bottleneck duration");
v3239Monitor.ClearQueueBlock(v3239FoodFacility);
v3239Now += TimeSpan.FromMinutes(6);
v3239Monitor.NoteBlockedRecipe(v3239FoodFacility, "queue_full");
Check(v3239Monitor.Snapshot().Single().QueueFullEpisodes == 2 &&
      v3239Monitor.Snapshot().Single().RepeatedIssue,
    "V3.2.39 a new blocked episode after verified clearance counts as recurrence");
v3239Monitor.NoteBlockedRecipe(v3239FoodFacility, "invalid_recipe_state");
v3239Monitor.NoteStalledWork(v3239FoodFacility);
v3239Monitor.NoteUncertainBatch(v3239FoodFacility);
v3239Snapshot = v3239Monitor.Snapshot().Single();
Check(v3239Snapshot.RecipeUnavailableEpisodes == 1 &&
      v3239Snapshot.StalledWorkEvents == 1 &&
      v3239Snapshot.UncertainBatchAttempts == 1 &&
      v3239Snapshot.LastKnownQueueDepth == 1 &&
      v3239Snapshot.LastKnownCompleted == 1,
    "V3.2.39 distinguishes unavailable recipe, stall, uncertain batch and CLI queue snapshot");
Check(v3239Monitor.Snapshot().Count == 1,
    "V3.2.39 local tracker never creates duplicate entries for repeated facility");
for (int v3239Index = 0; v3239Index < 24; v3239Index++)
    v3239Monitor.NoteStalledWork("시설-" + v3239Index);
Check(v3239Monitor.Snapshot().Count == 16,
    "V3.2.39 limits incident memory to sixteen distinct facilities");

// V3.2.39: real scheduler masked-food evidence must be reflected in the
// per-facility tracker, without authorizing any registration.
// V3.2.35: make same-item CLI observations safe under overlapping reads.
var v3235Logs = new List<string>();
var v3235State = new LifeSkillManagerState(recoveryDelay: (_, _) => Task.CompletedTask);
v3235State.Log += v3235Logs.Add;
v3235State.BeginStage(LifeSkillStage.Gathering, "overlapping CLI inventory");
var v3235SlowResult = new TaskCompletionSource<long>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var v3235Old = v3235State.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "물이 든 병",
    (_, _) => v3235SlowResult.Task, CancellationToken.None);
long v3235New = await v3235State.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "물이 든 병",
    (_, _) => Task.FromResult(19L), CancellationToken.None);
v3235SlowResult.SetResult(11);
bool v3235LateDiscarded = false;
try { _ = await v3235Old; }
catch (InvalidOperationException ex)
{
    v3235LateDiscarded =
        ex.Message.Contains("이전 CLI 응답을 폐기", StringComparison.Ordinal);
}
Check(v3235New == 19 && v3235LateDiscarded &&
      v3235State.TryGetFreshEvidence(
          LifeSkillStage.Gathering, "물이 든 병", out var v3235Evidence) &&
      v3235Evidence?.Quantity == 19,
    "V3.2.35 preserves the newer same-item CLI read and rejects a late old reply");
var v3235Snapshot = v3235State.DiagnosticsSnapshot();
Check(v3235Snapshot.Requests == 2 && v3235Snapshot.Attempts == 2 &&
      v3235Snapshot.StaleInventoryReplies == 1 &&
      v3235Snapshot.Recovered == 0 && v3235Snapshot.Failed == 0,
    "V3.2.35 read statistics distinguish requests, attempts and stale results");

var v3235OtherSlow = new TaskCompletionSource<long>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var v3235Independent = v3235State.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "쇠고기",
    (_, _) => v3235OtherSlow.Task, CancellationToken.None);
long v3235Parallel = await v3235State.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "달걀",
    (_, _) => Task.FromResult(8L), CancellationToken.None);
v3235OtherSlow.SetResult(7);
Check(v3235Parallel == 8 && await v3235Independent == 7 &&
      v3235State.TryGetFreshEvidence(
          LifeSkillStage.Gathering, "쇠고기", out var v3235Beef) &&
      v3235Beef?.Quantity == 7,
    "V3.2.35 unrelated item observations can finish independently");

int v3235ReadCalls = 0;
long v3235Recovered = await v3235State.ReadWithRecoveryAsync<long>(
    LifeSkillStage.Gathering, "activity",
    _ => { if (++v3235ReadCalls == 1) throw new TimeoutException();
           return Task.FromResult(1L); },
    CancellationToken.None);
Check(v3235Recovered == 1 && v3235ReadCalls == 2 &&
      v3235State.DiagnosticsSnapshot().Recovered == 1 &&
      v3235State.DiagnosticsSnapshot().Attempts == 6,
    "V3.2.35 recovered read requires exactly one additional CLI attempt");

int v3235InvalidCalls = 0;
bool v3235Invalid = false;
try
{
    _ = await v3235State.ReadWithRecoveryAsync<long>(
        LifeSkillStage.Gathering, "invalid-recipe",
        _ => { v3235InvalidCalls++; throw new InvalidDataException("semantic mismatch"); },
        CancellationToken.None);
}
catch (InvalidDataException) { v3235Invalid = true; }
Check(v3235Invalid && v3235InvalidCalls == 1 &&
      v3235State.DiagnosticsSnapshot().Failed == 1,
    "V3.2.35 semantic state failures do not cause unsafe transport retries");

v3235State.BeginStage(LifeSkillStage.Crafting, "handoff");
Check(v3235Logs.Any(x => x.Contains("[조회 통계]", StringComparison.Ordinal) &&
      x.Contains("늦은 재고 응답 폐기 1", StringComparison.Ordinal)) &&
      v3235State.DiagnosticsSnapshot().Requests == 0 &&
      !v3235State.TryGetFreshEvidence(
          LifeSkillStage.Gathering, "물이 든 병", out _),
    "V3.2.35 reports previous-stage diagnostics and invalidates observations on handoff");

var v3235DelayManager = new LifeSkillManagerState(
    recoveryDelay: (_, _) => Task.CompletedTask);
v3235DelayManager.BeginStage(LifeSkillStage.Altering, "late retry test");
int v3235LateReads = 0;
bool v3235RetryRejected = false;
try
{
    _ = await v3235DelayManager.ReadWithRecoveryAsync<long>(
        LifeSkillStage.Altering, "scheduler-works",
        _ => { v3235LateReads++;
               v3235DelayManager.BeginStage(LifeSkillStage.Crafting, "handoff");
               throw new IOException("transient-but-stale"); },
        CancellationToken.None);
}
catch (InvalidOperationException) { v3235RetryRejected = true; }
Check(v3235RetryRejected && v3235LateReads == 1,
    "V3.2.35 refuses transient retry after a stage switch");

// V3.2.34: long-run heartbeat is diagnostic-only and bounded.
var v3234Now = DateTimeOffset.Parse("2026-10-19T00:00:00+00:00");
var v3234Heartbeat = new LifeSkillLongRunHeartbeat(
    TimeSpan.FromMinutes(1), () => v3234Now);
Check(v3234Heartbeat.ShouldReport("gathering", 0) &&
      !v3234Heartbeat.ShouldReport("gathering", 0),
    "V3.2.34 logs first gathering state but suppresses identical polls");
v3234Now += TimeSpan.FromSeconds(59);
Check(!v3234Heartbeat.ShouldReport("gathering", 0),
    "V3.2.34 suppresses repeated inventory logs under the heartbeat interval");
v3234Now += TimeSpan.FromSeconds(2);
Check(v3234Heartbeat.ShouldReport("gathering", 0),
    "V3.2.34 emits a periodic diagnostic heartbeat without changing inventory");
Check(v3234Heartbeat.ShouldReport("gathering", 1) &&
      v3234Heartbeat.ShouldReport("combat", 1),
    "V3.2.34 never hides confirmed progress or a gathering/combat transition");
v3234Now -= TimeSpan.FromMinutes(2);
Check(v3234Heartbeat.ShouldReport("combat", 1),
    "V3.2.34 resets logging after a wall-clock rollback");
bool v3234InvalidQuantity = false;
try { _ = v3234Heartbeat.ShouldReport("gathering", -1); }
catch (InvalidDataException) { v3234InvalidQuantity = true; }
Check(v3234InvalidQuantity, "V3.2.34 does not bless negative diagnostic inventory");
LifeSkillLongRunCombatPolicy.ThrowIfStalled(
    LifeSkillLongRunCombatPolicy.MaximumConsecutivePolls - 1, "field");
bool v3234CombatTimeout = false;
try
{
    LifeSkillLongRunCombatPolicy.ThrowIfStalled(
        LifeSkillLongRunCombatPolicy.MaximumConsecutivePolls, "field");
}
catch (InvalidOperationException) { v3234CombatTimeout = true; }
Check(v3234CombatTimeout,
    "V3.2.34 detects excessive combat-only polling without issuing inputs");

// An F9 facility stuck behind another full queue must not go silent overnight.
var v3234LaneNow = DateTimeOffset.Parse("2026-10-19T00:00:00+00:00");
var v3234Deferrals = new MultiAlteringLaneDeferralTracker(
    TimeSpan.FromMinutes(2), () => v3234LaneNow);
Check(v3234Deferrals.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 2) is null,
    "V3.2.34 starts a blocked facility observation without game input");
v3234LaneNow += TimeSpan.FromMinutes(2);
var v3234First = v3234Deferrals.ObserveBlocked(
    "식재료 가공 시설", "밥", "queue_full", 0, 2);
v3234LaneNow += TimeSpan.FromMinutes(9);
Check(v3234First?.Contains("최초 보류 경고", StringComparison.Ordinal) == true &&
      v3234Deferrals.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 2) is null,
    "V3.2.34 limits blocked-lane diagnostic rate");
v3234LaneNow += TimeSpan.FromMinutes(1);
var v3234Reminder = v3234Deferrals.ObserveBlocked(
    "식재료 가공 시설", "밥", "queue_full", 0, 2);
Check(v3234Reminder?.Contains("장시간 보류 재알림", StringComparison.Ordinal) == true &&
      v3234Reminder.Contains("대기 720초", StringComparison.Ordinal),
    "V3.2.34 re-reports persistent empty-lane starvation every ten minutes");
v3234LaneNow -= TimeSpan.FromMinutes(20);
Check(v3234Deferrals.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 2) is null,
    "V3.2.34 resets uncertain elapsed time after wall-clock adjustment");
v3234LaneNow += TimeSpan.FromMinutes(1);
Check(v3234Deferrals.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 1, 2) is null,
    "V3.2.34 clears starvation on confirmed live F9 work");

// V3.2.33: no blind replay of a screen action after uncertain outcome.
var v3233Fence = new LifeSkillWorkFence();
var v3233Log = new List<string>();
v3233Fence.Log += v3233Log.Add;
var pendingCraft = v3233Fence.Begin("craft:밥");
bool v3233ConcurrentBlocked = false;
try { _ = v3233Fence.Begin("craft:밥"); }
catch (InvalidOperationException) { v3233ConcurrentBlocked = true; }
Check(v3233ConcurrentBlocked, "V3.2.33 blocks overlapping craft on the same item");

bool v3233MissingBlocked = false;
try { v3233Fence.ConfirmInventoryIncrease(pendingCraft, 5, 6, 2); }
catch (InvalidOperationException) { v3233MissingBlocked = true; }
Check(v3233MissingBlocked, "V3.2.33 rejects insufficient inventory gain instead of claiming completion");
v3233Fence.MarkUncertain(pendingCraft, "craft_result_unconfirmed");
bool v3233UncertainBlocked = false;
try { _ = v3233Fence.Begin("craft:밥"); }
catch (InvalidOperationException) { v3233UncertainBlocked = true; }
Check(v3233UncertainBlocked &&
      v3233Log.Any(x => x.Contains("중복 실행 차단", StringComparison.Ordinal)),
    "V3.2.33 quarantines an uncertain craft action without retry");

var knownGather = v3233Fence.Begin("gather:물이 든 병");
v3233Fence.ConfirmInventoryIncrease(knownGather, 5, 9, 3);
var nextKnownGather = v3233Fence.Begin("gather:물이 든 병");
bool v3233OldReplyBlocked = false;
try { v3233Fence.ConfirmInventoryIncrease(knownGather, 5, 12, 3); }
catch (InvalidOperationException) { v3233OldReplyBlocked = true; }
Check(v3233OldReplyBlocked, "V3.2.33 rejects late confirmation from an earlier attempt");
v3233Fence.ConfirmInventoryIncrease(nextKnownGather, 9, 12, 3);
Check(true, "V3.2.33 permits a second gathering only after previous positive proof");

var noMutationF9 = v3233Fence.Begin("alter:food/밥/1");
v3233Fence.ConfirmBatchReturn(noMutationF9, 0, false);
var nextF9 = v3233Fence.Begin("alter:food/밥/1");
v3233Fence.ConfirmBatchReturn(nextF9, 1, false);
Check(true, "V3.2.33 preserves existing F9 nonterminal scheduling and confirmed registration evidence");

var brokenF9 = v3233Fence.Begin("alter:metal/철/1");
bool v3233NegativeRegistration = false;
try { v3233Fence.ConfirmBatchReturn(brokenF9, -1, false); }
catch (InvalidOperationException) { v3233NegativeRegistration = true; }
v3233Fence.MarkUncertain(brokenF9, "alter_batch_unconfirmed");
bool v3233FailedF9Blocked = false;
try { _ = v3233Fence.Begin("alter:metal/철/1"); }
catch (InvalidOperationException) { v3233FailedF9Blocked = true; }
Check(v3233NegativeRegistration && v3233FailedF9Blocked,
    "V3.2.33 blocks invalid registration deltas and refuses blind F9 replay");

bool v3233NegativeInventory = false;
var invalidGather = v3233Fence.Begin("gather:쇠고기");
try { v3233Fence.ConfirmInventoryIncrease(invalidGather, -1, 10, 1); }
catch (InvalidOperationException) { v3233NegativeInventory = true; }
v3233Fence.MarkUncertain(invalidGather, "invalid_inventory");
Check(v3233NegativeInventory, "V3.2.33 rejects invalid or negative CLI inventory evidence");

// V3.2.32: per-stage transport recovery never reissues a game action.
var v3232Log = new List<string>();
var v3232Manager = new LifeSkillManagerState(
    recoveryDelay: (_, _) => Task.CompletedTask);
v3232Manager.Log += v3232Log.Add;
v3232Manager.BeginStage(LifeSkillStage.Gathering, "gather");
int v3232Reads = 0;
long v3232Recovered = await v3232Manager.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "물이 든 병",
    (_, _) =>
    {
        v3232Reads++;
        if (v3232Reads == 1) throw new TimeoutException("transient CLI");
        return Task.FromResult(4L);
    }, CancellationToken.None);
Check(v3232Recovered == 4 && v3232Reads == 2 &&
      v3232Log.Any(x => x.Contains("화면 입력 없음", StringComparison.Ordinal)) &&
      v3232Log.Any(x => x.Contains("새 CLI 응답 확인", StringComparison.Ordinal)),
    "shared manager retries only a transient read once and logs fresh recovery");

v3232Reads = 0;
bool v3232Persistent = false;
try
{
    _ = await v3232Manager.ReadWithRecoveryAsync<long>(
        LifeSkillStage.Gathering, "activity",
        ct => { v3232Reads++; throw new IOException("link unstable"); },
        CancellationToken.None);
}
catch (IOException) { v3232Persistent = true; }
Check(v3232Persistent && v3232Reads == 2 &&
      v3232Log.Any(x => x.Contains("분류=transient_read", StringComparison.Ordinal)),
    "persistent CLI I/O failure stops after two reads and retains error category");

v3232Reads = 0;
bool v3232Mismatch = false;
try
{
    _ = await v3232Manager.ReadWithRecoveryAsync<long>(
        LifeSkillStage.Gathering, "activity",
        ct => { v3232Reads++; throw new InvalidOperationException("state mismatch"); },
        CancellationToken.None);
}
catch (InvalidOperationException) { v3232Mismatch = true; }
Check(v3232Mismatch && v3232Reads == 1 &&
      v3232Log.Any(x => x.Contains("분류=state_conflict", StringComparison.Ordinal)),
    "game state conflict never becomes an automatic retry");

int v3232NegativeReads = 0;
bool v3232InvalidData = false;
try
{
    _ = await v3232Manager.ReadFreshCountAsync(
        LifeSkillStage.Gathering, "물이 든 병",
        (_, _) => { v3232NegativeReads++; return Task.FromResult(-1L); },
        CancellationToken.None);
}
catch (InvalidDataException) { v3232InvalidData = true; }
Check(v3232InvalidData && v3232NegativeReads == 1,
    "semantically invalid inventory cannot be masked by transport recovery");

var v3232Tcs = new TaskCompletionSource<long>(
    TaskCreationOptions.RunContinuationsAsynchronously);
var v3232Pending = v3232Manager.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "물이 든 병", (_, _) => v3232Tcs.Task,
    CancellationToken.None);
v3232Manager.BeginStage(LifeSkillStage.Crafting, "handoff to crafting");
v3232Tcs.SetResult(77);
bool v3232Late = false;
try { _ = await v3232Pending; }
catch (InvalidOperationException) { v3232Late = true; }
Check(v3232Late &&
      !v3232Manager.TryGetFreshEvidence(LifeSkillStage.Crafting, "물이 든 병", out _),
    "late gathering result does not pollute new crafting epoch");

int v3232CancelledReads = 0;
using (var v3232Cancel = new CancellationTokenSource())
{
    v3232Manager.BeginStage(LifeSkillStage.Altering, "retry cancellation");
    var v3232WithCancel = new LifeSkillManagerState(
        recoveryDelay: (_, token) => { v3232Cancel.Cancel(); return Task.CompletedTask; });
    v3232WithCancel.BeginStage(LifeSkillStage.Altering, "retry cancellation");
    bool cancellationProtected = false;
    try
    {
        _ = await v3232WithCancel.ReadWithRecoveryAsync<long>(
            LifeSkillStage.Altering, "recipes",
            _ => { v3232CancelledReads++; throw new TimeoutException(); },
            v3232Cancel.Token);
    }
    catch (OperationCanceledException) { cancellationProtected = true; }
    Check(cancellationProtected && v3232CancelledReads == 1,
        "F10 cancellation prevents a second read and does not retry input");
}

// Deferred queue configuration must not authorize a game operation.
// F9 checks a fresh CLI recipe snapshot before any screen input.
var deferredPlan = new AlteringPlan("목재 가공 시설", "목재+", 12, 3, false);
var deferredRecipe = new AlteringRecipe("목재+", false, 3,
    "unknown_after_patch", Array.Empty<AlteringIngredient>(), "목재 가공 시설");
string deferredError = "";
try { AlteringQueueStartGuard.EnsureStartable(new[] { deferredPlan }, new[] { deferredRecipe }); }
catch (InvalidOperationException ex) { deferredError = ex.Message; }
Check(deferredError.Contains("F9 시작을 보류합니다", StringComparison.Ordinal) &&
      deferredError.Contains("unknown_after_patch", StringComparison.Ordinal),
    "F9 blocks deferred recipe when latest CLI still reports unavailable without shortages");
AlteringQueueStartGuard.EnsureStartable(new[] { deferredPlan },
    new[] { deferredRecipe with { Alterable = true } });
Check(true, "F9 allows verified recipe after CLI readiness recovers");
AlteringQueueStartGuard.EnsureStartable(new[] { deferredPlan },
    new[] { deferredRecipe with { MissingIngredients =
        new[] { new AlteringIngredient("통나무", 3, 0) } } });
Check(true, "F9 preserves explicit shortage route for the existing recursive resolver");
string changedYieldError = "";
try { AlteringQueueStartGuard.EnsureStartable(new[] { deferredPlan },
        new[] { deferredRecipe with { ProducedPerWork = 7, Alterable = true } }); }
catch (InvalidOperationException ex) { changedYieldError = ex.Message; }
Check(changedYieldError.Contains("등록 당시와 달라져", StringComparison.Ordinal),
    "F9 blocks an altered recipe identity before session creation");


 // V3.2.29: F9 re-reads a contradictory global queue_full before creating
 // any manifest or clicking. A clean, later CLI snapshot may unblock safely.
 int refreshReads = 0, refreshWorkReads = 0, refreshDelays = 0;
 var refreshEvidence = new List<string>();
 var refreshed = await AlteringQueueStartGuard.EnsureStartableWithRefreshAsync(
     new[] { deferredPlan },
     _ => Task.FromResult<IReadOnlyList<AlteringRecipe>>(
         new[] { ++refreshReads == 1
             ? deferredRecipe with { Reason = "queue_full" }
             : deferredRecipe with { Alterable = true, Reason = null } }),
     _ => { refreshWorkReads++; return Task.FromResult<IReadOnlyList<AlteringWork>>(
         Array.Empty<AlteringWork>()); },
     (_, _) => { refreshDelays++; return Task.CompletedTask; },
     CancellationToken.None, refreshEvidence.Add);
 Check(refreshReads == 2 && refreshWorkReads == 2 && refreshDelays == 1 &&
       refreshed.Recipes.Single().Alterable &&
       refreshEvidence.Count(x => x.Contains("queue_full/시설별 슬롯 불일치", StringComparison.Ordinal)) == 1 &&
       refreshEvidence.Count(x => x.Contains("[CLI][가공 시작 품목별 진단]", StringComparison.Ordinal)) == 2,
     "F9 retries unverified queue_full read-only and accepts a fresh ready snapshot");

 int persistReads = 0, persistDelays = 0;
 bool persistentRejected = false;
 try
 {
     await AlteringQueueStartGuard.EnsureStartableWithRefreshAsync(
         new[] { deferredPlan },
         _ => { persistReads++; return Task.FromResult<IReadOnlyList<AlteringRecipe>>(
             new[] { deferredRecipe with { Reason = "queue_full" } }); },
         _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
         (_, _) => { persistDelays++; return Task.CompletedTask; },
         CancellationToken.None);
 }
 catch (InvalidOperationException ex)
 {
     persistentRejected = ex.Message.Contains("queue_full", StringComparison.Ordinal);
 }
 Check(persistentRejected && persistReads == 3 && persistDelays == 2,
     "persistent unverified queue_full refuses F9 after exactly two bounded retries");

 int shortageReads = 0;
 var explicitShortage = await AlteringQueueStartGuard.EnsureStartableWithRefreshAsync(
     new[] { deferredPlan },
     _ => { shortageReads++; return Task.FromResult<IReadOnlyList<AlteringRecipe>>(
         new[] { deferredRecipe with { Reason = "not_enough_ingredient",
             MissingIngredients = new[] { new AlteringIngredient("통나무", 2, 0) } } }); },
     _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
     (_, _) => throw new Exception("Explicit shortage must not re-poll"),
     CancellationToken.None);
 Check(shortageReads == 1 && explicitShortage.Recipes.Single().MissingIngredients.Count == 1,
     "F9 preserves explicit shortage resolver authority without masked-queue retry");

 int otherFailureReads = 0;
 bool otherFailureRejected = false;
 try
 {
     await AlteringQueueStartGuard.EnsureStartableWithRefreshAsync(
         new[] { deferredPlan },
         _ => { otherFailureReads++; return Task.FromResult<IReadOnlyList<AlteringRecipe>>(
             new[] { deferredRecipe }); },
         _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
         (_, _) => throw new Exception("Unrelated refusal must not re-poll"),
         CancellationToken.None);
 }
 catch (InvalidOperationException ex)
 {
     otherFailureRejected = ex.Message.Contains("unknown_after_patch", StringComparison.Ordinal);
 }
 Check(otherFailureRejected && otherFailureReads == 1,
     "F9 does not disguise other CLI recipe refusals as transient queue_full");

 using (var cancelRead = new CancellationTokenSource())
 {
     int cancelledReads = 0;
     bool cancelledWithoutNewInput = false;
     try
     {
         await AlteringQueueStartGuard.EnsureStartableWithRefreshAsync(
             new[] { deferredPlan },
             _ => { cancelledReads++; return Task.FromResult<IReadOnlyList<AlteringRecipe>>(
                 new[] { deferredRecipe with { Reason = "queue_full" } }); },
             _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
             (_, _) => { cancelRead.Cancel(); return Task.CompletedTask; },
             cancelRead.Token);
     }
     catch (OperationCanceledException) { cancelledWithoutNewInput = true; }
     Check(cancelledWithoutNewInput && cancelledReads == 1,
         "F10 cancellation stops queue_full refresh before a second snapshot");
 }

// 2026-10-15 18:57 live CLI log: ALL 91 recipes were reported
// Alterable=false, Reason=queue_full. Existing seven-slot queues must be
// managed by the old wait -> verified receipt -> refill engine, not rejected.
var fullLaneRecipe = deferredRecipe with { Reason = "queue_full" };
var fullLane = Enumerable.Range(0, 7)
    .Select(i => new AlteringWork("이전 목재 작업", deferredPlan.FacilityName,
        i == 0 ? "Completed" : "InProgress", i == 0, i == 0 ? 0 : 120L))
    .ToArray();
AlteringQueueStartGuard.EnsureStartable(
    new[] { deferredPlan }, new[] { fullLaneRecipe }, fullLane);
Check(true, "F9 accepts queue_full with exactly seven observed works for existing wait/receipt/refill");
AlteringQueueStartGuard.EnsureStartable(
    new[] { deferredPlan }, new[] { fullLaneRecipe },
    fullLane.Select(x => x with { State = "Completed", IsCompleted = true,
        RemainingSeconds = 0 }).ToArray());
Check(true, "F9 accepts queue_full with seven finished works for guarded receipt first");
string disputedSlots = "";
try
{
    AlteringQueueStartGuard.EnsureStartable(
        new[] { deferredPlan }, new[] { fullLaneRecipe }, fullLane.Take(6).ToArray());
}
catch (InvalidOperationException ex) { disputedSlots = ex.Message; }
Check(disputedSlots.Contains("6/7칸", StringComparison.Ordinal),
    "F9 rejects contradictory queue_full for a six-slot facility instead of guessing");
string missingSlotEvidence = "";
try
{
    AlteringQueueStartGuard.EnsureStartable(
        new[] { deferredPlan }, new[] { fullLaneRecipe });
}
catch (InvalidOperationException ex) { missingSlotEvidence = ex.Message; }
Check(missingSlotEvidence.Contains("조회되지 않음", StringComparison.Ordinal),
    "F9 requires fresh read-only slot evidence to authorize queue_full handoff");

var otherFacilityPlan = new AlteringPlan("금속 가공 시설", "철괴", 9, 3, false);
var otherFacilityRecipe = new AlteringRecipe("철괴", false, 3, "queue_full",
    Array.Empty<AlteringIngredient>(), "금속 가공 시설");
// Live CLI reports queue_full for *every* recipe while one 7-slot lane is full.
// Readiness preflight must allow the selected full lane to drain, without
// authorizing registration into the other empty lane before the full one clears.
AlteringQueueStartGuard.EnsureStartable(
    new[] { deferredPlan, otherFacilityPlan },
    new[] { fullLaneRecipe, otherFacilityRecipe }, fullLane);
Check(true, "global queue_full can hand off selected existing full lane even if another selected facility is empty");
int unexpectedSecondaryRegistration = 0;
using (var cancelAfterWait = new CancellationTokenSource())
{
    try
    {
        await new MultiAlteringCoordinator(
            deferOtherLanesWhileQueueFull: true).RunAsync(
            new[] { deferredPlan, otherFacilityPlan },
            (item, budget, ct) =>
            {
                unexpectedSecondaryRegistration++;
                return Task.FromResult(false);
            },
            ct => Task.FromResult<IReadOnlyList<AlteringWork>>(fullLane),
            (time, ct) =>
            {
                cancelAfterWait.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cancelAfterWait.Token);
    }
    catch (OperationCanceledException) when (cancelAfterWait.IsCancellationRequested) { }
}
Check(unexpectedSecondaryRegistration == 0,
    "global queue_full waits for selected old seven-slot lane without attempting new registration at another facility");

// 2026-10-18 live log: metal and cloth both 7/7 while food is not
// filled. The old startup queue_full flag permanently skipped the food
// facility despite positive fresh readiness; 43+ minutes of wait logs ensued.
// A live CLI positive result must permit this independent lane to reach the
// normal receipt/material/registration gates, never bypass those gates.
var clothPlan = new AlteringPlan("옷감 가공 시설", "상급 옷감", 9, 3, false);
var foodPlan = new AlteringPlan("식재료 가공 시설", "밥", 9, 3, false);
var clothFull = Enumerable.Range(0, 7).Select(_ =>
    new AlteringWork("상급 옷감", clothPlan.FacilityName, "InProgress", false, 7200L)).ToArray();
var foodOldCompleted = new AlteringWork(
    "기존 음식", foodPlan.FacilityName, "Completed", true, 0);
var blockedWithTwoFull = fullLane.Concat(clothFull)
    .Append(foodOldCompleted).ToArray();
var readyFoodRecipe = new AlteringRecipe(
    foodPlan.DisplayName, true, foodPlan.ProducedPerWork, null,
    Array.Empty<AlteringIngredient>(), foodPlan.FacilityName);
var maskedFoodRecipe = readyFoodRecipe with { Alterable = false, Reason = "queue_full" };
var positiveFoodRuns = new List<string>();
var independentLogs = new List<string>();
int independentRecipeReads = 0;
using (var stopOnReadyFood = new CancellationTokenSource())
{
    try
    {
        var scheduler = new MultiAlteringCoordinator(deferOtherLanesWhileQueueFull: true);
        scheduler.Log += independentLogs.Add;
        await scheduler.RunAsync(
            new[] { deferredPlan, clothPlan, foodPlan },
            (plan, _, ct) =>
            {
                positiveFoodRuns.Add(plan.DisplayName);
                stopOnReadyFood.Cancel();
                ct.ThrowIfCancellationRequested();
                return Task.FromResult(false);
            },
            ct => Task.FromResult<IReadOnlyList<AlteringWork>>(blockedWithTwoFull),
            (_, ct) => { stopOnReadyFood.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            stopOnReadyFood.Token,
            readRecipes: ct =>
            {
                ct.ThrowIfCancellationRequested();
                independentRecipeReads++;
                return Task.FromResult<IReadOnlyList<AlteringRecipe>>(
                    new[] { readyFoodRecipe });
            });
    }
    catch (OperationCanceledException) when (stopOnReadyFood.IsCancellationRequested) { }
}
Check(independentRecipeReads > 0 &&
      positiveFoodRuns.SequenceEqual(new[] { "밥" }) &&
      independentLogs.Any(x => x.Contains("독립 시설 진행 승인", StringComparison.Ordinal)),
    "F9 permits live-ready rice lane despite two unrelated 7/7 lanes (real registration guards remain responsible)");

int blockedFoodRuns = 0;
var maskedLogs = new List<string>();
var maskedBottleneckScheduler = new MultiAlteringCoordinator(
    deferOtherLanesWhileQueueFull: true);
using (var stopOnMaskedFood = new CancellationTokenSource())
{
    try
    {
        var scheduler = maskedBottleneckScheduler;
        scheduler.Log += maskedLogs.Add;
        await scheduler.RunAsync(
            new[] { deferredPlan, clothPlan, foodPlan },
            (plan, _, ct) =>
            {
                blockedFoodRuns++;
                return Task.FromResult(false);
            },
            ct => Task.FromResult<IReadOnlyList<AlteringWork>>(blockedWithTwoFull),
            (_, ct) => { stopOnMaskedFood.Cancel(); ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
            stopOnMaskedFood.Token,
            readRecipes: ct => Task.FromResult<IReadOnlyList<AlteringRecipe>>(
                new[] { maskedFoodRecipe }));
    }
    catch (OperationCanceledException) when (stopOnMaskedFood.IsCancellationRequested) { }
}
Check(blockedFoodRuns == 0 &&
      maskedLogs.Any(x => x.Contains("독립 시설 보류", StringComparison.Ordinal) &&
          x.Contains("queue_full", StringComparison.Ordinal)),
    "F9 still refuses rice click while CLI only reports queue_full, and logs explicit skip reason");
Check(maskedBottleneckScheduler.Bottlenecks.Snapshot().Any(x =>
      x.Facility == foodPlan.FacilityName && x.QueueFullEpisodes == 1 &&
      x.UncertainBatchAttempts == 0),
    "V3.2.39 masked food lane appears in scheduler diagnostic without clicking");

int missingRecipeRuns = 0;
bool recipeIdentityBlocked = false;
try
{
    await new MultiAlteringCoordinator(deferOtherLanesWhileQueueFull: true).RunAsync(
        new[] { deferredPlan, clothPlan, foodPlan },
        (_, _, ct) => { missingRecipeRuns++; return Task.FromResult(false); },
        ct => Task.FromResult<IReadOnlyList<AlteringWork>>(blockedWithTwoFull),
        (_, ct) => Task.CompletedTask,
        CancellationToken.None,
        readRecipes: ct => Task.FromResult<IReadOnlyList<AlteringRecipe>>(Array.Empty<AlteringRecipe>()));
}
catch (InvalidOperationException ex) when (ex.Message.Contains("제법 동일성 확인 실패", StringComparison.Ordinal))
{ recipeIdentityBlocked = true; }
Check(recipeIdentityBlocked && missingRecipeRuns == 0,
    "F9 fails closed when a live recipe cannot be uniquely identified before independent registration");


// V3.2.31: one observation epoch for gathering, intermediate processing,
// and crafting. The evidence is never permission to click.
var sharedClock = new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero);
var sharedManager = new LifeSkillManagerState(
    () => sharedClock, TimeSpan.FromSeconds(3));
int observedReads = 0;
Task<long> ReadObserved(string _, CancellationToken ct)
{
    ct.ThrowIfCancellationRequested();
    observedReads++;
    return Task.FromResult(7L);
}
sharedManager.BeginStage(LifeSkillStage.Altering, "F9");
_ = await sharedManager.ReadFreshCountAsync(
    LifeSkillStage.Altering, "물이 든 병", ReadObserved, CancellationToken.None);
Check(sharedManager.TryGetFreshEvidence(
    LifeSkillStage.Altering, "물이 든 병", out var previous) &&
    previous?.Quantity == 7, "confirmed CLI count is evidence in altering stage");
sharedManager.BeginStage(LifeSkillStage.Gathering, "raw gather");
Check(!sharedManager.TryGetFreshEvidence(
    LifeSkillStage.Altering, "물이 든 병", out _), "gathering handoff clears old inventory state");
bool stageMismatch = false;
try
{
    await sharedManager.ReadFreshCountAsync(LifeSkillStage.Altering,
        "물이 든 병", ReadObserved, CancellationToken.None);
}
catch (InvalidOperationException) { stageMismatch = true; }
Check(stageMismatch && observedReads == 1,
    "wrong stage blocks using old CLI state before requesting more data");
_ = await sharedManager.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "물이 든 병", ReadObserved, CancellationToken.None);
sharedClock = sharedClock.AddSeconds(4);
Check(!sharedManager.TryGetFreshEvidence(
    LifeSkillStage.Gathering, "물이 든 병", out _),
    "old observations expire and cannot masquerade as fresh state");
sharedManager.BeginStage(LifeSkillStage.Crafting, "quest resume");
_ = await sharedManager.ReadFreshCountAsync(
    LifeSkillStage.Crafting, "물이 든 병", ReadObserved, CancellationToken.None);
Check(observedReads == 3 &&
    sharedManager.TryGetFreshEvidence(LifeSkillStage.Crafting, "물이 든 병", out _),
    "crafting sees fresh source data after handoff");
bool lateReplyBlocked = false;
sharedManager.BeginStage(LifeSkillStage.Altering, "intermediate");
try
{
    await sharedManager.ReadFreshCountAsync(
        LifeSkillStage.Altering, "물이 든 병",
        (_, _) => { sharedManager.BeginStage(LifeSkillStage.Gathering, "overlap");
            return Task.FromResult(12L); }, CancellationToken.None);
}
catch (InvalidOperationException) { lateReplyBlocked = true; }
Check(lateReplyBlocked && !sharedManager.TryGetFreshEvidence(
    LifeSkillStage.Altering, "물이 든 병", out _),
    "out-of-order reply after a stage switch fails closed");
sharedManager.Invalidate("F10 stop");
Check(!sharedManager.TryGetFreshEvidence(
    LifeSkillStage.Gathering, "물이 든 병", out _),
    "cancelled or unknown location invalidates shared evidence");
bool impossibleCountRejected = false;
sharedManager.BeginStage(LifeSkillStage.Gathering, "fresh read");
try { await sharedManager.ReadFreshCountAsync(
    LifeSkillStage.Gathering, "물이 든 병",
    (_, _) => Task.FromResult(-1L), CancellationToken.None); }
catch (InvalidDataException) { impossibleCountRejected = true; }
Check(impossibleCountRejected, "negative CLI inventory cannot be trusted");
// V3.2.30: unattended empty food lane is observable without any new input.
var observedTime = DateTimeOffset.Parse("2026-10-19T00:00:00+00:00");
var laneDeferral = new MultiAlteringLaneDeferralTracker(
    TimeSpan.FromMinutes(2), () => observedTime);
Check(laneDeferral.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 2) is null,
    "starvation timer starts on first confirmed empty blocked food lane");
observedTime += TimeSpan.FromSeconds(119);
Check(laneDeferral.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 2) is null,
    "starvation timer does not invent early progress or game input");
observedTime += TimeSpan.FromSeconds(2);
string? stalledFood = laneDeferral.ObserveBlocked(
    "식재료 가공 시설", "밥", "queue_full", 0, 2);
Check(stalledFood is not null &&
      stalledFood.Contains("시설 누락 감시", StringComparison.Ordinal) &&
      stalledFood.Contains("타 시설 포화 2개", StringComparison.Ordinal) &&
      laneDeferral.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 2) is null,
    "blocked empty food lane produces exactly one diagnostic warning at threshold");
laneDeferral.Reset("식재료 가공 시설");
observedTime += TimeSpan.FromSeconds(121);
Check(laneDeferral.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 2) is null,
    "a verified lane reset prevents stale starvation warning");
observedTime += TimeSpan.FromSeconds(121);
Check(laneDeferral.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 1, 2) is null,
    "live food job is not misdiagnosed as an empty lane");
observedTime += TimeSpan.FromSeconds(121);
Check(laneDeferral.ObserveBlocked("식재료 가공 시설", "밥", "queue_full", 0, 0) is null,
    "absence of other full facilities clears queue_full starvation evidence");

// Each selected recipe must appear in the preflight evidence without
// guessing materials that the CLI has not exposed.
var stateEvidence = MultiAlteringMaterialPreflightPolicy.DescribeSelectedEvidence(
    new[] { deferredPlan, foodPlan },
    new[] { deferredRecipe with { Reason = "queue_full" },
        readyFoodRecipe with { Alterable = false, Reason = "not_enough_ingredient",
            MissingIngredients = new[] { new AlteringIngredient("물이 든 병", 2, 0) } } },
    fullLane);
Check(stateEvidence.Count == 2 &&
      stateEvidence[0].Contains("queue_full", StringComparison.Ordinal) &&
      stateEvidence[0].Contains("미확정", StringComparison.Ordinal) &&
      stateEvidence[1].Contains("물이 든 병", StringComparison.Ordinal) &&
      stateEvidence[1].Contains("CLI 확정 부족", StringComparison.Ordinal),
    "global preflight reports masked recipe separately from confirmed food shortage");

// Local incident evidence must be bounded and must not include identity,
// arbitrary exception messages or the full CLI payload.
string incidentDir = Path.Combine(Path.GetTempPath(),
    "mabiauto-v3230-" + Guid.NewGuid().ToString("N"));
try
{
    var snapshotPath = await MultiAlteringIncidentSnapshot.CaptureAsync(
        new[] { foodPlan }, nameof(InvalidOperationException),
        _ => Task.FromResult<IReadOnlyList<AlteringRecipe>>(new[]
        {
            readyFoodRecipe with { Alterable = false,
                Reason = "not_enough_ingredient",
                MissingIngredients = new[]
                { new AlteringIngredient("물이 든 병", 2, 0) } }
        }),
        _ => Task.FromResult<IReadOnlyList<AlteringWork>>(new[]
        { new AlteringWork("밥", foodPlan.FacilityName, "InProgress", false, 60) }),
        incidentDir, CancellationToken.None,
        recoveryPatterns: v3236History,
        recoveryDecision: LifeSkillRecoveryDecisionPolicy.ForIncident(
            new InvalidOperationException("sensitive exception detail")),
        facilityBottlenecks: new[] {
            v3239Monitor.Snapshot().Single(x => x.Facility == v3239FoodFacility)
        }.Concat(v3239Monitor.Snapshot().Where(x => x.Facility != v3239FoodFacility))
         .ToArray());
    var snapshot = File.ReadAllText(snapshotPath);
    using var parsedSnapshot = System.Text.Json.JsonDocument.Parse(snapshot);
    var rootSnapshot = parsedSnapshot.RootElement;
    Check(rootSnapshot.GetProperty("selected")[0].GetProperty("missing")[0]
              .GetProperty("name").GetString() == "물이 든 병" &&
          rootSnapshot.GetProperty("lanes")[0].GetProperty("facility")
              .GetString() == "식재료 가공 시설" &&
          rootSnapshot.GetProperty("recipeRead").GetString() == "ok" &&
          !snapshot.Contains("AccountCode", StringComparison.Ordinal) &&
          !snapshot.Contains("CharacterId", StringComparison.Ordinal),
        "error incident keeps local read-only recipe/queue evidence without user identity");
    Check(rootSnapshot.GetProperty("recovery").GetProperty("category").GetString()
              == "state_conflict" &&
          rootSnapshot.GetProperty("recovery").GetProperty("gameValidated").GetBoolean()
              == false &&
          rootSnapshot.GetProperty("recovery").GetProperty("historicalPatterns")
              .GetArrayLength() > 0 &&
          snapshot.Contains("unconfirmed_action", StringComparison.Ordinal) &&
          !snapshot.Contains("sensitive exception detail", StringComparison.Ordinal),
        "V3.2.36 incident links bounded recovery labels without raw exception text or claimed game validation");
    var facilityEvidence = rootSnapshot.GetProperty("facilityBottlenecks");
    Check(facilityEvidence.GetArrayLength() == 8 &&
          facilityEvidence[0].GetProperty("facility").GetString() == v3239FoodFacility &&
          facilityEvidence[0].GetProperty("queueFullEpisodes").GetInt64() == 2 &&
          facilityEvidence[0].GetProperty("repeatedIssue").GetBoolean() &&
          !facilityEvidence[0].GetProperty("confirmedOmission").GetBoolean() &&
          !facilityEvidence[0].GetProperty("permitsGameAction").GetBoolean() &&
          !snapshot.Contains("invalid_recipe_state", StringComparison.Ordinal),
        "V3.2.39 F9 incident stores bounded facility diagnoses without raw reason or fictional proof");
    var readFailure = await MultiAlteringIncidentSnapshot.CaptureAsync(
        new[] { foodPlan }, nameof(InvalidOperationException),
        _ => throw new InvalidDataException("secret payload"),
        _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
        incidentDir, CancellationToken.None);
    var failureText = File.ReadAllText(readFailure);
    Check(failureText.Contains("InvalidDataException", StringComparison.Ordinal) &&
          !failureText.Contains("secret payload", StringComparison.Ordinal),
        "failed CLI read retains failure category only, never raw failure payload");
    for (int i = 0; i < 21; i++)
        _ = await MultiAlteringIncidentSnapshot.CaptureAsync(
            new[] { foodPlan }, "Synthetic",
            _ => Task.FromResult<IReadOnlyList<AlteringRecipe>>(Array.Empty<AlteringRecipe>()),
            _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
            incidentDir, CancellationToken.None);
    Check(Directory.GetFiles(incidentDir, "incident-*.json").Length == 20,
        "local incident retention limits on-disk snapshots to last 20");
}
finally
{
    if (Directory.Exists(incidentDir)) Directory.Delete(incidentDir, recursive: true);
}

// 2026-10-11 11:53/11:54 F9 runtime stop: do not reject a stale Reviving
// flag after the move when the pre-click field guard already tolerates it.
var travelIdle = new GatheringActivity(
    IsDead: false, IsReviving: false, IsInCombat: false,
    IsAutoPlaying: false, IsAutoTraveling: false,
    IsDialoguePlaying: false, IsWaitingForSelection: false,
    DungeonState: "NotInDungeon", IsInBattlefield: false,
    IsPlayingTutorial: false, IsInScenario: false,
    IsPlayingPerformance: false, IsPlayingMiniGame: false,
    IsHousingEditMode: false, MainButtonState: "Compass",
    HasTarget: false, AvailableInteractionType: "None",
    LastRunningInteractionType: "Altering");
Check(AlteringTravelActivityPolicy.AssessAfterMove(travelIdle) ==
      AlteringTravelActivityVerdict.SafeToObserve,
    "F9 travel: ordinary field safe to observe");
Check(GatheringSafetyPolicy.IsSafeField(travelIdle with { IsReviving = true }) &&
      AlteringTravelActivityPolicy.AssessAfterMove(travelIdle with { IsReviving = true }) ==
      AlteringTravelActivityVerdict.SafeToObserve,
    "F9 travel: stale CLI Reviving has the same result before and after move");
Check(AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { IsDialoguePlaying = true }) ==
      AlteringTravelActivityVerdict.AwaitPossibleTravelDialog &&
      AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { IsWaitingForSelection = true }) ==
      AlteringTravelActivityVerdict.AwaitPossibleTravelDialog &&
      AlteringTravelActivityPolicy.MaxAmbiguousFrames == 3,
    "F9 travel: ambiguous CLI dialogue/selection only waits three no-input frames");
Check(AlteringTravelActivityPolicy.CanConfirmVerifiedTravelDialog(
        travelIdle with { IsDialoguePlaying = true, IsWaitingForSelection = true }) &&
      !GatheringSafetyPolicy.IsSafeField(
        travelIdle with { IsDialoguePlaying = true }),
    "F9 travel: only an independently verified managed travel popup can tolerate dialogue flags");
Check(AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { IsDead = true }) == AlteringTravelActivityVerdict.Unsafe &&
      AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { IsInCombat = true }) == AlteringTravelActivityVerdict.Unsafe &&
      AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { DungeonState = "InDungeon" }) == AlteringTravelActivityVerdict.Unsafe &&
      AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { IsInCombat = true, IsDialoguePlaying = true }) ==
        AlteringTravelActivityVerdict.Unsafe,
    "F9 travel: genuine death/combat/dungeon never enters transient-dialog wait");
Check(!AlteringTravelActivityPolicy.CanConfirmVerifiedTravelDialog(
        travelIdle with { IsInCombat = true, IsDialoguePlaying = true }) &&
      !AlteringTravelActivityPolicy.CanConfirmVerifiedTravelDialog(
        travelIdle with { IsDead = true }) &&
      !AlteringTravelActivityPolicy.CanConfirmVerifiedTravelDialog(
        travelIdle with { IsAutoTraveling = true }) &&
      !AlteringTravelActivityPolicy.CanConfirmVerifiedTravelDialog(
        travelIdle with { IsPlayingMiniGame = true }) &&
      !AlteringTravelActivityPolicy.CanConfirmVerifiedTravelDialog(
        travelIdle with { IsWaitingForSelection = true, DungeonState = "InDungeon" }),
    "F9 travel: actual hazards and existing travel cannot authorize popup Space");
Check(AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { IsAutoTraveling = true, IsDialoguePlaying = true }) ==
      AlteringTravelActivityVerdict.Unsafe &&
      AlteringTravelActivityPolicy.AssessAfterMove(
        travelIdle with { IsDialoguePlaying = true, IsPlayingTutorial = true }) ==
      AlteringTravelActivityVerdict.Unsafe,
    "F9 travel: travel/dialogue and tutorial/dialogue combinations fail closed");
Check(AlteringTravelActivityPolicy.Describe(
        travelIdle with { IsDialoguePlaying = true }).Contains("Dialogue=True") &&
      AlteringTravelActivityPolicy.Describe(travelIdle).Contains("Dungeon=NotInDungeon"),
    "F9 travel: errors expose exact safety bits instead of misleading generic messages");

// Existing F9 travel and receipt safety regressions follow unchanged.
// V3.1.76: F9 long-distance facility travel expires by elapsed time,
 // not 120 UI/CLI poll iterations. No extension on loading or confirmation.
Check(AlteringFacilityTravelConfirmPolicy.ManagedTravelTimeout == TimeSpan.FromMinutes(5) &&
      AlteringFacilityTravelConfirmPolicy.IsManagedTravelWithinLimit(TimeSpan.Zero) &&
      AlteringFacilityTravelConfirmPolicy.IsManagedTravelWithinLimit(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59)) &&
      !AlteringFacilityTravelConfirmPolicy.IsManagedTravelWithinLimit(TimeSpan.FromMinutes(5)) &&
      !AlteringFacilityTravelConfirmPolicy.IsManagedTravelWithinLimit(TimeSpan.FromMinutes(6)) &&
      !AlteringFacilityTravelConfirmPolicy.IsManagedTravelWithinLimit(TimeSpan.FromSeconds(-1)),
    "V3.1.76 F9 managed travel allows <5 elapsed minutes and times out at >=5");

// Next-release Telegram status: only the exact Korean read-only alias is
// accepted. Unsafe controls must remain explicit slash commands.
Check(TelegramCommandParser.Parse("상태") == "/status" &&
      TelegramCommandParser.Parse(" 상태 ") == "/status" &&
      TelegramCommandParser.Parse("/status") == "/status" &&
      TelegramCommandParser.Parse("/status@MabiAutoBot") == "/status" &&
      TelegramCommandParser.Parse("/stop") == "/stop" &&
      TelegramCommandParser.Parse("중지") is null &&
      TelegramCommandParser.Parse("상태확인") is null &&
      TelegramCommandParser.Parse("/상태") is null &&
      TelegramCommandParser.Parse("상태 지금") is null,
    "Telegram Korean 상태 is EXACT read-only /status alias; control commands remain slash-only");

var telegramNow = DateTimeOffset.Now;
var telegramSnapshot = new MultiAlteringTelegramSnapshot(
    new[]
    {
        new MultiAlteringTelegramItem("목재 가공 시설", "목재+", 0, 100,
            7, 34, "가공 진행", 600)
    },
    "[다중가공] 병렬 대기 · 다음 확인 약 20초",
    telegramNow.AddSeconds(-7));
var partiallyReady = new AlteringWork[]
{
    new("목재+", "목재 가공 시설", "Completed", true, 0),
    new("목재+", "목재 가공 시설", "Completed", true, 0),
    new("목재+", "목재 가공 시설", "Completed", true, 0),
    new("목재+", "목재 가공 시설", "InProgress", false, 135),
    new("목재+", "목재 가공 시설", "InProgress", false, 180),
    new("목재+", "목재 가공 시설", "InProgress", false, 220),
    new("목재+", "목재 가공 시설", "InProgress", false, 250)
};
string partialTelegram = MultiAlteringTelegramStatus.Format(
    telegramSnapshot, partiallyReady, telegramNow, null);
Check(partialTelegram.Contains("목재+: 0/100개") &&
      partialTelegram.Contains("등록 7/34회") &&
      partialTelegram.Contains("사용 7/7칸") &&
      partialTelegram.Contains("완료 3 · 진행 4") &&
      partialTelegram.Contains("일부 완료, 시설 전체 완료 전 수령 보류") &&
      partialTelegram.Contains("2분 15초") &&
      partialTelegram.Contains("병렬 대기"),
    "Telegram status reports pending completed slots and next completion even when confirmed output is 0");
var allReady = partiallyReady.Select(w => w with
{
    State = "Completed",
    IsCompleted = true,
    RemainingSeconds = 0
}).ToArray();
string readyTelegram = MultiAlteringTelegramStatus.Format(
    telegramSnapshot, allReady, telegramNow, null);
Check(readyTelegram.Contains("완료 7 · 진행 0") &&
      readyTelegram.Contains("모두 받기 수령 조건 확인"),
    "Telegram detailed F9 status identifies whole seven-slot batch ready for collect");
string unavailableTelegram = MultiAlteringTelegramStatus.Format(
    telegramSnapshot, null, telegramNow, "CLI 응답 시간 초과");
Check(unavailableTelegram.Contains("실시간 조회 불가") &&
      unavailableTelegram.Contains("작업 0건으로 취급하지 않음") &&
      !unavailableTelegram.Contains("사용 0/7칸"),
    "Telegram CLI query failure stays unknown instead of fabricating zero active works");

Check(AlteringText.UniqueOcrAlias("강철괴", new[]{"강철괴", "합금강괴"}) == "강철과", "specific OCR alias is catalog checked");
Check(AlteringText.UniqueOcrAlias("강철괴", new[]{"강철괴", "강철과"}) is null && AlteringText.UniqueOcrAlias("목재+", new[]{"목재+"}) is null, "ambiguous and unsupported OCR aliases blocked");
Check(AlteringDetailPolicy.IsConfirmed(true, false, false, false),
    "detail accepts an exact title match");
Check(AlteringDetailPolicy.IsConfirmed(false, true, true, false),
    "detail accepts materials plus free action when title OCR misses");
Check(AlteringDetailPolicy.IsConfirmed(false, true, false, true),
    "detail accepts materials plus remote paid action when title OCR misses");
Check(!AlteringDetailPolicy.IsConfirmed(false, true, false, false),
    "materials alone do not confirm a recipe detail screen");
Check(!AlteringDetailPolicy.IsConfirmed(false, false, true, false),
    "action alone does not confirm a recipe detail screen");
Check(AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
        fieldOnly: true, confirmationVisible: false, autoTraveling: false,
        safeField: true, receiptWorkCountBefore: 4),
    "14:23 receipt regression: verified field after completion permits one safe facility re-entry");
Check(!AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
        fieldOnly: true, confirmationVisible: true, autoTraveling: false,
        safeField: true, receiptWorkCountBefore: 4),
    "completion or travel popup blocks field re-entry");
Check(!AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
        fieldOnly: true, confirmationVisible: false, autoTraveling: true,
        safeField: true, receiptWorkCountBefore: 4),
    "real auto-travel blocks field re-entry");
Check(!AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
        fieldOnly: true, confirmationVisible: false, autoTraveling: null,
        safeField: true, receiptWorkCountBefore: 4),
    "unknown CLI state blocks field re-entry");
Check(!AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
        fieldOnly: false, confirmationVisible: false, autoTraveling: false,
        safeField: true, receiptWorkCountBefore: 4),
    "other altering screens block field re-entry");
Check(!AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
        fieldOnly: true, confirmationVisible: false, autoTraveling: false,
        safeField: false, receiptWorkCountBefore: 4),
    "unsafe field blocks facility re-entry");
Check(!AlteringReceiptPolicy.CanRecoverFieldAfterCompletion(
        fieldOnly: true, confirmationVisible: false, autoTraveling: false,
        safeField: true, receiptWorkCountBefore: null),
    "unknown receipt baseline forbids facility recovery");
Check(AlteringReceiptPolicy.IsProvenReceiptAfterReopen(4, 0) &&
      AlteringReceiptPolicy.IsProvenReceiptAfterReopen(4, 3),
    "confirmed same-recipe queue drop allows continuation after reopening");
Check(!AlteringReceiptPolicy.IsProvenReceiptAfterReopen(4, 4) &&
      !AlteringReceiptPolicy.IsProvenReceiptAfterReopen(4, 5) &&
      !AlteringReceiptPolicy.IsProvenReceiptAfterReopen(4, null),
    "reopened facility cannot falsely confirm unchanged/increased/unknown receipt");
// V3.2.5 05:24 managed receipt: a false/stale facility title may be
// ignored for modal classification ONLY with whole-lane CLI receipt,
// positive completion layout/title, and a fresh confirmed idle activity.
Check(AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, true, true, true, false, false, false) &&
      AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, true, true, false, true, false, false),
    "05:24 manager full receipt + proven reward/title may disambiguate facility title");
Check(!AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    false, true, true, true, false, false, false) &&
      !AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, false, true, true, false, false, false) &&
      !AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, true, false, true, false, false, false) &&
      !AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, true, true, false, false, false, false) &&
      !AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, true, true, true, false, true, false) &&
      !AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, true, true, true, false, false, true) &&
      !AlteringReceiptPolicy.IsVerifiedManagedCompletionOverlay(
    true, true, true, true, false, false, null),
    "05:24 unknown/partial CLI, unrelated green popup, travel, or movement veto overlay");

Check(AlteringReceiptPolicy.CanConfirmReceiptFacilityReturn(
        facilityHeaderVisible: true, completionModalVisible: false,
        travelDialogVisible: false, autoTraveling: false),
    "M5: two clean facility frames with known idle CLI may prove post-receipt return");
Check(!AlteringReceiptPolicy.CanConfirmReceiptFacilityReturn(
        facilityHeaderVisible: true, completionModalVisible: true,
        travelDialogVisible: false, autoTraveling: false),
    "M5: facility title behind completion modal must not prove onsite");
Check(!AlteringReceiptPolicy.CanConfirmReceiptFacilityReturn(
        facilityHeaderVisible: true, completionModalVisible: false,
        travelDialogVisible: true, autoTraveling: false),
    "M5: travel dialog blocks onsite confirmation");
Check(!AlteringReceiptPolicy.CanConfirmReceiptFacilityReturn(
        facilityHeaderVisible: true, completionModalVisible: false,
        travelDialogVisible: false, autoTraveling: true) &&
      !AlteringReceiptPolicy.CanConfirmReceiptFacilityReturn(
        facilityHeaderVisible: true, completionModalVisible: false,
        travelDialogVisible: false, autoTraveling: null),
    "M5: active or unknown CLI movement prevents false onsite reuse");
Check(!AlteringReceiptPolicy.CanConfirmReceiptFacilityReturn(
        facilityHeaderVisible: false, completionModalVisible: false,
        travelDialogVisible: false, autoTraveling: false),
    "M5: missing facility title cannot be treated as completed return");
// F04: management cannot close an unrelated green result/confirmation,
// even if the character is otherwise idle and the facility header is absent.
Check(AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, true, false, false, false),
    "F04 managed completed-title plus green/idle positively authorizes close");
Check(!AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, false, false, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, true, true, false, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, true, false, true, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, true, false, false, true) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, true, false, false, null) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        false, true, false, false, false),
    "F04 unrecognized green/travel/facility/unknown CLI may not authorize Space");

// 2026-10-09 07:00 cyan cards and 18:13 gray unframed rewards are both
// legitimate completion screens. Managed fallback requires a fresh full-lane
// CLI 7->0 receipt, green confirmation, no travel/facility and known idle;
// OCR or cyan-only anchors must not be the deciding veto.
Check(AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, true, true, false, false, false) &&
      AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, true, false, true, false, false, false),
    "F04 visual reward layout OR title OCR confirms genuine result after CLI empty lane");
Check(AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, false, true, false, false, false),
    "F04 18:13 gray rewards: manager accepts full 7->0 plus green/idle without title OCR or cyan anchors");
Check(!AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, true, false, false, false, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, false, false, false, false, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, true, true, true, false, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, true, true, false, true, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, true, true, false, false, true) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, false, true, true, false, false, null) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        false, false, true, true, false, false, false) &&
      !AlteringReceiptPolicy.CanCloseManagedCompletionResult(
        true, true, false, false, false, false, false),
    "F04 manager fallback blocks no CLI receipt, missing green, facility, travel, or unknown activity");

Check(AlteringReceiptPolicy.CanConfirmCompletion(true, false, false, false),
    "real altering completion result can authorize confirmation");
Check(!AlteringReceiptPolicy.CanConfirmCompletion(true, false, true, false),
    "facility travel confirmation dialog never authorizes receipt Space");
Check(!AlteringReceiptPolicy.CanConfirmCompletion(true, false, false, true),
    "AutoTraveling state never authorizes completion Space");
Check(!AlteringReceiptPolicy.CanConfirmCompletion(true, true, false, false),
    "facility screen itself is not a completion result");
Check(AlteringReceiptPolicy.CanRetryCompletionClose(true, false, false, false),
    "unchanged real completion modal may receive exactly one guarded close retry");
Check(!AlteringReceiptPolicy.CanRetryCompletionClose(true, true, false, false),
    "facility screen never receives a completion-close retry");
Check(!AlteringReceiptPolicy.CanRetryCompletionClose(true, false, true, false) &&
      !AlteringReceiptPolicy.CanRetryCompletionClose(true, false, false, true),
    "travel dialog or active travel blocks completion-close retry");
Check(AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
        greenConfirmVisible: true,
        facilityVisible: false,
        travelDialogVisible: false),
    "CLI-confirmed receipt may close a proven completion modal without consulting stale AutoTraveling");
Check(!AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
        greenConfirmVisible: true,
        facilityVisible: true,
        travelDialogVisible: false),
    "CLI-confirmed receipt never closes the facility screen");
Check(!AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
        greenConfirmVisible: true,
        facilityVisible: false,
        travelDialogVisible: true),
    "real facility travel confirmation still blocks CLI-confirmed receipt completion Space");
Check(!AlteringReceiptPolicy.CanConfirmCliReceiptCompletion(
        greenConfirmVisible: false,
        facilityVisible: false,
        travelDialogVisible: false),
    "CLI receipt evidence alone cannot authorize Space without the green completion modal");
Check(AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
        greenConfirmVisible: true,
        facilityVisible: false,
        travelDialogVisible: false),
    "CLI-confirmed completion modal may receive the existing single guarded close retry");
Check(!AlteringReceiptPolicy.CanRetryCliReceiptCompletionClose(
        greenConfirmVisible: true,
        facilityVisible: false,
        travelDialogVisible: true),
    "travel dialog blocks CLI-confirmed completion-close retry");
Check(!AlteringRemoteProcessGuard.ShouldBlock(true, false),
    "single-frame remote processing spike is ignored after clean confirmation");
Check(!AlteringRemoteProcessGuard.ShouldBlock(false, true),
    "non-consecutive remote processing spike never blocks input");
Check(AlteringRemoteProcessGuard.ShouldBlock(true, true),
    "two consecutive remote processing observations block input");
Check(!AlteringRemoteProcessGuard.ShouldBlock(
        true, true,
        onsiteFacilityConfirmed: true,
        firstOnsiteActionVisible: true,
        secondOnsiteActionVisible: true),
    "two-frame remote OCR is ignored only when the same proven onsite facility also shows the onsite action button in both frames");
Check(AlteringRemoteProcessGuard.ShouldBlock(
        true, true,
        onsiteFacilityConfirmed: false,
        firstOnsiteActionVisible: true,
        secondOnsiteActionVisible: true),
    "onsite action visuals cannot override remote OCR without a proven onsite facility");
Check(AlteringRemoteProcessGuard.ShouldBlock(
        true, true,
        onsiteFacilityConfirmed: true,
        firstOnsiteActionVisible: true,
        secondOnsiteActionVisible: false),
    "one missing onsite action frame preserves the two-frame remote safety veto");


// L1: child screen cannot act as an independent manager-owned onsite cache.
Check(AlteringScreenOnsiteCachePolicy.MayTrustForReceipt(
        AlteringFacilityEntryDirective.Automatic, "금속 가공 시설", "금속 가공 시설") &&
      !AlteringScreenOnsiteCachePolicy.MayTrustForReceipt(
        AlteringFacilityEntryDirective.Automatic, "금속 가공 시설", "목재 가공 시설"),
    "L1: Automatic-only receipt cache remains facility-scoped");
Check(!AlteringScreenOnsiteCachePolicy.MayTrustForReceipt(
        AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
        "금속 가공 시설", "금속 가공 시설") &&
      !AlteringScreenOnsiteCachePolicy.MayTrustForReceipt(
        AlteringFacilityEntryDirective.FreshMoveRequired,
        "금속 가공 시설", "금속 가공 시설"),
    "L1: managed receipt ignores lower screen cache even when facility names match");
Check(AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry(
        AlteringFacilityEntryDirective.Automatic, "금속 가공 시설") == "금속 가공 시설" &&
      AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry(
        AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite, "금속 가공 시설") is null &&
      AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry(
        AlteringFacilityEntryDirective.FreshMoveRequired, "금속 가공 시설") is null,
    "L1: successful managed reuse/fresh screen entries cannot grant local onsite authority");
Check(AlteringScreenOnsiteCachePolicy.AfterVerifiedReceiptReturn(
        managedReceipt: false, facilityName: "목재 가공 시설") == "목재 가공 시설" &&
      AlteringScreenOnsiteCachePolicy.AfterVerifiedReceiptReturn(
        managedReceipt: true, facilityName: "목재 가공 시설") is null,
    "L1: managed receipt return reports success but never repopulates Automatic cache");
Check(AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
        AlteringFacilityEntryDirective.Automatic,
        moveButtonVisible: true),
    "single altering preserves the legacy move-button safety veto");
Check(!AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
        AlteringFacilityEntryDirective.Automatic,
        moveButtonVisible: false),
    "single altering without a move-button visual keeps the normal recipe route");
Check(!AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
        AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
        moveButtonVisible: true),
    "H1: coordinator-confirmed same-facility reuse ignores the always-visible move button during recipe selection");
Check(!AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
        AlteringFacilityEntryDirective.FreshMoveRequired,
        moveButtonVisible: true),
    "H1: a completed manager-directed fresh travel cannot be overturned by the move button during recipe selection");
Check(AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false,
        facilityVisible: true,
        moveButtonVisible: AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
            AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite, true),
        retryAlreadyUsed: false),
    "H1: a dropped first card click can retry once under confirmed reuse, even with the always-present move button");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false,
        facilityVisible: false,
        moveButtonVisible: AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
            AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite, true),
        retryAlreadyUsed: false),
    "H1: coordinator reuse cannot override a missing facility header before retry");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: true,
        facilityVisible: true,
        moveButtonVisible: AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
            AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite, true),
        retryAlreadyUsed: false),
    "H1: delayed detail opening must prevent a duplicate click even with coordinator reuse");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false,
        facilityVisible: true,
        moveButtonVisible: AlteringFacilityEntryPolicy.ShouldVetoRecipeMoveButton(
            AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite, true),
        retryAlreadyUsed: true),
    "H1: coordinator reuse never weakens one-shot recipe retry limit");

Check(AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false,
        facilityVisible: true,
        moveButtonVisible: false,
        retryAlreadyUsed: false),
    "fixed recipe retry is allowed only when the same on-site facility list is still visible");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: true,
        facilityVisible: true,
        moveButtonVisible: false,
        retryAlreadyUsed: false),
    "delayed detail opening blocks a duplicate fixed recipe click");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false,
        facilityVisible: false,
        moveButtonVisible: false,
        retryAlreadyUsed: false),
    "unknown transition screen never receives a fixed recipe retry");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false,
        facilityVisible: true,
        moveButtonVisible: true,
        retryAlreadyUsed: false),
    "remote facility state never receives a fixed recipe retry");
Check(!AlteringFixedRecipeRetryPolicy.ShouldRetry(
        detailVisible: false,
        facilityVisible: true,
        moveButtonVisible: false,
        retryAlreadyUsed: true),
    "fixed recipe retry is strictly one-shot");

Check(AlteringFacilityTravelConfirmPolicy.ShouldConfirmAfterMoveClick(
        travelPopupVisual: true,
        confirmationSpaceCount: 0,
        departureAlreadySeen: false),
    "19:21 regression: a center-screen travel confirmation appearing after the move click receives Space");
Check(AlteringFacilityTravelConfirmPolicy.ShouldConfirmAfterMoveClick(
        travelPopupVisual: true,
        confirmationSpaceCount: 1,
        departureAlreadySeen: false),
    "same proven travel popup may receive exactly one bounded Space retry if it remains visible");
Check(!AlteringFacilityTravelConfirmPolicy.ShouldConfirmAfterMoveClick(
        travelPopupVisual: false,
        confirmationSpaceCount: 0,
        departureAlreadySeen: false) &&
      !AlteringFacilityTravelConfirmPolicy.ShouldConfirmAfterMoveClick(
        travelPopupVisual: true,
        confirmationSpaceCount: 2,
        departureAlreadySeen: false) &&
      !AlteringFacilityTravelConfirmPolicy.ShouldConfirmAfterMoveClick(
        travelPopupVisual: true,
        confirmationSpaceCount: 0,
        departureAlreadySeen: true),
    "travel Space is blocked without the popup, after two attempts, or once departure is already proven");
// Near-instant real movement (already standing by the machine) can finish
// before the first sampled frame. Click alone or disappearing OCR is NEVER
// enough; require a proven pre-click remote button plus a distinct close-X
// after click, no remaining remote move button or modal, and idle CLI.
Check(AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        moveClickSent: true, preClickRemoteMoveButtonConfirmed: true,
        facilityVisible: true, onsiteCloseVisible: true,
        remoteMoveButtonVisible: false, autoTraveling: false,
        anyModalVisible: false),
    "20:56 near-instant relocation has independent post-click onsite evidence");
Check(!AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        moveClickSent: false, preClickRemoteMoveButtonConfirmed: true,
        facilityVisible: true, onsiteCloseVisible: true,
        remoteMoveButtonVisible: false, autoTraveling: false,
        anyModalVisible: false) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        true, false, true, true, false, false, false) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        true, true, true, false, false, false, false) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        true, true, true, true, true, false, false) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        true, true, true, true, false, null, false) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        true, true, true, true, false, true, false) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        true, true, true, true, false, false, true) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedInstantArrival(
        true, true, false, true, false, false, false),
    "F01 near-instant branch rejects click-only, absent X, remote UI, unknown/busy CLI or any modal");
// F01 regression: two unrelated "loading" API rejections with a visible
// facility title, or two OCR title misses with readable nontravel CLI, are
// NOT movement evidence. Only positive auto-travel or consecutive correlated
// reject+header-absent frames after the exact move click can prove departure.
Check(!AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedMoveTransition(
        moveClickSent: true, observedAutoTraveling: false,
        consecutiveLoadingWithMissingHeader: 0) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedMoveTransition(
        moveClickSent: true, observedAutoTraveling: false,
        consecutiveLoadingWithMissingHeader: 1) &&
      !AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedMoveTransition(
        moveClickSent: false, observedAutoTraveling: true,
        consecutiveLoadingWithMissingHeader: 2),
    "F01 standalone stale UI/OCR, single transient loading, or no click may not confirm movement");
Check(AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedMoveTransition(
        moveClickSent: true, observedAutoTraveling: true,
        consecutiveLoadingWithMissingHeader: 0) &&
      AlteringFacilityTravelConfirmPolicy.HasVerifiedManagedMoveTransition(
        moveClickSent: true, observedAutoTraveling: false,
        consecutiveLoadingWithMissingHeader: 2),
    "F01 confirmed post-click auto-travel or consecutive correlated loading-and-departure authorizes candidate only");
Check(!AlteringFacilityTravelConfirmPolicy.IsManagedFreshArrivalObservation(
        facilityVisible: true, autoTraveling: false,
        moveClickSent: true, transitionObserved: false) &&
      AlteringFacilityTravelConfirmPolicy.IsManagedFreshArrivalObservation(
        facilityVisible: true, autoTraveling: false,
        moveClickSent: true, transitionObserved: true) &&
      !AlteringFacilityTravelConfirmPolicy.IsManagedFreshArrivalObservation(
        facilityVisible: false, autoTraveling: false,
        moveClickSent: true, transitionObserved: true),
    "F01 final return must include same facility title, idle CLI and previously verified travel transition");

Check(AlteringFacilityTravelConfirmPolicy.MaxTravelConfirmationSpaces == 2,
    "travel confirmation Space retry is strictly bounded");
// V3.1.68 real 19:35 (800x1000): post-(85,235) travel popup is visually
// confirmed, but Korean "던버튼으로 이동할까요?" OCR returns no match.
// Manager must accept ONLY the move-correlated popup with known idle CLI.
Check(AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        moveClickSent: true, travelPopupVisual: true, confirmationSpaceCount: 0,
        departureAlreadySeen: false, autoTraveling: false) &&
      AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        true, true, 1, false, false),
    "V3.1.68 manager accepts original V3.1.47 post-click visual popup even when OCR misses");
Check(!AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        false, true, 0, false, false) &&
      !AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        true, false, 0, false, false) &&
      !AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        true, true, 2, false, false) &&
      !AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        true, true, 0, true, false) &&
      !AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        true, true, 0, false, true) &&
      !AlteringFacilityTravelConfirmPolicy.CanConfirmManagedTravelPopupAfterMoveClick(
        true, true, 0, false, null),
    "V3.1.68 manager refuses generic green, no click, exhausted retry, departure, active or unknown CLI");

Check(AlteringFacilityTravelConfirmPolicy.IsOnsiteObservation(
        facilityVisible: true,
        moveButtonVisible: false,
        autoTraveling: false),
    "facility arrival candidate requires visible facility, no move button and proven non-travel CLI");
Check(!AlteringFacilityTravelConfirmPolicy.IsOnsiteObservation(
        facilityVisible: true,
        moveButtonVisible: false,
        autoTraveling: null) &&
      !AlteringFacilityTravelConfirmPolicy.IsOnsiteObservation(
        facilityVisible: true,
        moveButtonVisible: true,
        autoTraveling: false) &&
      !AlteringFacilityTravelConfirmPolicy.IsOnsiteObservation(
        facilityVisible: true,
        moveButtonVisible: false,
        autoTraveling: true),
    "unknown CLI, visible move button or active travel never proves facility arrival");
Check(!AlteringFacilityTravelConfirmPolicy.HasStableOnsiteEvidence(
        AlteringFacilityTravelConfirmPolicy.RequiredOnsiteStableFrames - 1,
        TimeSpan.FromSeconds(4)) &&
      !AlteringFacilityTravelConfirmPolicy.HasStableOnsiteEvidence(
        AlteringFacilityTravelConfirmPolicy.RequiredOnsiteStableFrames,
        TimeSpan.FromMilliseconds(2999)),
    "16:43 arrival regression: facility arrival cannot be authorized by a short move-button disappearance");
Check(AlteringFacilityTravelConfirmPolicy.HasStableOnsiteEvidence(
        AlteringFacilityTravelConfirmPolicy.RequiredOnsiteStableFrames,
        TimeSpan.FromSeconds(3)),
    "18:16 sufficient-material regression: facility arrival requires the full stable frame and duration threshold");
Check(AlteringFacilityTravelConfirmPolicy.FinalOnsiteRecheckDelay >= TimeSpan.FromSeconds(1),
    "facility arrival keeps a delayed final recheck after the stable window");

Check(!AlteringReceiptPolicy.IsRemoteMoveButtonAfterReceiptMove(
        visualMoveButton: true,
        exactMoveLabelVisible: false),
    "04:05 receipt regression: post-move teal-only false positive is not remote proof");
Check(AlteringReceiptPolicy.IsRemoteMoveButtonAfterReceiptMove(
        visualMoveButton: true,
        exactMoveLabelVisible: true),
    "post-move receipt state still treats visual plus exact move label as remote");
Check(!AlteringReceiptPolicy.ShouldBlockReceiptForMoveButton(
        trustedOnsiteFacility: true,
        visualMoveButton: true,
        exactMoveLabelVisible: false),
    "trusted receipt onsite state ignores teal-only move false positive");
Check(AlteringReceiptPolicy.ShouldBlockReceiptForMoveButton(
        trustedOnsiteFacility: true,
        visualMoveButton: true,
        exactMoveLabelVisible: true) &&
      AlteringReceiptPolicy.ShouldBlockReceiptForMoveButton(
        trustedOnsiteFacility: false,
        visualMoveButton: true,
        exactMoveLabelVisible: false),
    "exact move label always blocks receipt and untrusted visual move evidence stays conservative");

Check(AlteringReceiptPolicy.IsCliReceiptConfirmed(7, 6),
    "same-item queue decrease confirms an altering receipt");
Check(AlteringReceiptPolicy.IsCliReceiptConfirmed(1, 0),
    "last same-item work disappearing confirms an altering receipt");
Check(!AlteringReceiptPolicy.IsCliReceiptConfirmed(7, 7) &&
      !AlteringReceiptPolicy.IsCliReceiptConfirmed(6, 7),
    "unchanged or increased same-item queue never confirms a receipt");

var plan = new AlteringPlan("금속 가공 시설", "강철괴", 100, 3, false);
Check(plan.RequiredWorks == 34 && plan.ExpectedQuantity == 102 && plan.MaximumWings == 0, "target rounding with zero-wing invariant");
Check(AlteringText.Normalize("목재 +") != AlteringText.Normalize("목재"), "plus variants stay distinct");
Check(AlteringText.IsCardCandidate("강철과", "강철괴"), "faint card is only a candidate for detail verification");
Check(!AlteringText.IsCardCandidate("상급 목재", "상급 목재+") && !AlteringText.IsCardCandidate("철괴(광석)","철괴(철 광석)"), "candidate matching preserves recipe qualifiers");
Check(new AlteringPlan(plan.FacilityName, "철괴(철 광석)", 1, 3, false).OutputName == "철괴", "ingredient-qualified recipe maps to output item");
var riceLayoutPlan = new AlteringPlan("식재료 가공 시설", "물에 불린 쌀", 1, 5, false)
    with { RecipeCount = 1 };
Check(AlteringRecipeLayout.TryGetFixedCenter(riceLayoutPlan, out var riceCenter) &&
      riceCenter == new System.Drawing.Point(340, 724),
    "food on-site layout pins soaked rice to row 3 column 2");
var woodPlusLayoutPlan = new AlteringPlan("목재 가공 시설", "목재+", 1, 3, false)
    with { RecipeCount = 1 };
Check(AlteringRecipeLayout.TryGetFixedCenter(woodPlusLayoutPlan, out var woodPlusCenter) &&
      woodPlusCenter == new System.Drawing.Point(340, 412),
    "wood on-site layout pins wood plus to row 1 column 2");
var steelLayoutPlan = new AlteringPlan("금속 가공 시설", "강철괴", 1, 3, false)
    with { RecipeCount = 1 };
Check(AlteringRecipeLayout.TryGetFixedCenter(steelLayoutPlan, out var steelCenter) &&
      steelCenter == new System.Drawing.Point(461, 412),
    "metal on-site layout pins steel ingot to row 1 column 3");
var leatherPlusLayoutPlan = new AlteringPlan("가죽 가공 시설", "가죽+", 1, 3, false)
    with { RecipeCount = 1 };
Check(AlteringRecipeLayout.TryGetFixedCenter(leatherPlusLayoutPlan, out var leatherPlusCenter) &&
      leatherPlusCenter == new System.Drawing.Point(340, 412),
    "leather on-site layout pins leather plus to row 1 column 2");
var clothPlusLayoutPlan = new AlteringPlan("옷감 가공 시설", "옷감+", 1, 3, false)
    with { RecipeCount = 1 };
Check(AlteringRecipeLayout.TryGetFixedCenter(clothPlusLayoutPlan, out var clothPlusCenter) &&
      clothPlusCenter == new System.Drawing.Point(461, 412),
    "fabric on-site layout pins cloth plus to row 1 column 3");
var duplicateWood1 = new AlteringPlan("목재 가공 시설", "최상급 목재", 1, 3, false, 1)
    with { RecipeCount = 2 };
var duplicateWood2 = duplicateWood1 with { RecipeOrdinal = 2 };
Check(AlteringRecipeLayout.TryGetFixedCenter(duplicateWood1, out var duplicateWoodCenter1) &&
      AlteringRecipeLayout.TryGetFixedCenter(duplicateWood2, out var duplicateWoodCenter2) &&
      duplicateWoodCenter1 == new System.Drawing.Point(461, 584) &&
      duplicateWoodCenter2 == new System.Drawing.Point(340, 756),
    "duplicate fixed-grid recipes preserve recipe ordinal");
Check(!AlteringRecipeLayout.IsFixedFacility("약품 가공 시설") &&
      AlteringRecipeLayout.IsSafeMedicineSearchGeometry() &&
      CraftingHubLayout.IsSafeSearchGeometry() &&
      AlteringRecipeLayout.ProcessingSearchIconPoint == CraftingHubLayout.ProductSearchIconPoint &&
      AlteringRecipeLayout.ProcessingSearchIconPoint == new System.Drawing.Point(30, 118) &&
      AlteringRecipeLayout.ProcessingSearchInputPoint == CraftingHubLayout.ProductSearchInputPoint &&
      AlteringRecipeLayout.ProcessingSearchInputPoint == new System.Drawing.Point(400, 862) &&
      AlteringRecipeLayout.ProcessingSearchFirstResultPoint == CraftingHubLayout.ProductFirstResultPoint &&
      AlteringRecipeLayout.ProcessingSearchFirstResultPoint == new System.Drawing.Point(218, 613) &&
      AlteringRecipeLayout.ProcessingSearchDialogArea == CraftingHubLayout.ProductSearchDialogArea &&
      AlteringRecipeLayout.ProcessingSearchResultArea == CraftingHubLayout.ProductSearchResultArea &&
      AlteringRecipeLayout.ProcessingSearchOpenChangeRatio == CraftingHubLayout.SearchDialogOpenChangeRatio &&
      AlteringRecipeLayout.ProcessingSearchResultChangeRatio == CraftingHubLayout.SearchResultChangeRatio,
    "medicine search exactly matches food crafting coordinates and evidence thresholds");
Check(AlteringFacilityLayout.IsSafeMoveGeometry() &&
      AlteringFacilityLayout.MoveButtonPoint == new System.Drawing.Point(85, 235) &&
      AlteringFacilityLayout.MoveButtonVisualArea == new System.Drawing.Rectangle(15, 205, 155, 65) &&
      AlteringFacilityLayout.MoveButtonAnchorArea == new System.Drawing.Rectangle(35, 218, 100, 34) &&
      AlteringFacilityLayout.OnsiteCloseVisualArea == new System.Drawing.Rectangle(744, 42, 44, 44),
    "facility move uses fixed broad shape + center-anchor geometry");
Check(AlteringFacilityLayout.ShouldAcceptMoveButton(
        onsiteCloseVisible: true,
        moveShapeVisible: true,
        moveAnchorVisible: true),
    "17:21 regression: ambiguous top-right X/currency match cannot veto a proven fixed move button");
Check(AlteringFacilityLayout.ShouldAcceptMoveButton(
        onsiteCloseVisible: false,
        moveShapeVisible: true,
        moveAnchorVisible: true),
    "real remote move shape plus fixed anchor is accepted");
Check(!AlteringFacilityLayout.ShouldAcceptMoveButton(
        onsiteCloseVisible: true,
        moveShapeVisible: true,
        moveAnchorVisible: false) &&
      !AlteringFacilityLayout.ShouldAcceptMoveButton(
        onsiteCloseVisible: false,
        moveShapeVisible: false,
        moveAnchorVisible: true),
    "on-site fragments or isolated anchor color cannot masquerade as the remote move button");

Check(AlteringFacilityResolver.Resolve(new AlteringRecipe("새록 버섯 진액", true, 5, null, Array.Empty<AlteringIngredient>())) == "약품 가공 시설" &&
      AlteringFacilityResolver.Resolve(new AlteringRecipe("튼튼 버섯 가루", true, 5, null, Array.Empty<AlteringIngredient>())) == "약품 가공 시설" &&
      AlteringFacilityResolver.Resolve(new AlteringRecipe("불꽃의 결정(석양 나비)", true, 3, null, Array.Empty<AlteringIngredient>())) == "약품 가공 시설",
    "medicine recipes resolve to medicine facility without FacilityName");
Check(AlteringFacilityResolver.Resolve(new AlteringRecipe("마요네즈", true, 3, null, Array.Empty<AlteringIngredient>())) == "식재료 가공 시설" &&
      AlteringFacilityResolver.Resolve(new AlteringRecipe("밀가루", true, 3, null, Array.Empty<AlteringIngredient>())) == "식재료 가공 시설" &&
      AlteringFacilityResolver.Resolve(new AlteringRecipe("치즈", true, 3, null, Array.Empty<AlteringIngredient>())) == "식재료 가공 시설" &&
      AlteringFacilityResolver.Resolve(new AlteringRecipe("면", true, 3, null, Array.Empty<AlteringIngredient>())) == "식재료 가공 시설" &&
      AlteringFacilityResolver.Resolve(new AlteringRecipe("생크림", true, 3, null, Array.Empty<AlteringIngredient>())) == "식재료 가공 시설",
    "food recipes resolve to food facility instead of showing only flour");

using (var metadataDoc = System.Text.Json.JsonDocument.Parse(
    """{"DisplayName":"미지 제법","Alterable":true,"ProducedPerWork":1,"Reason":null,"MissingIngredients":[{"DisplayName":"철괴","Required":1,"Owned":0}],"CategoryName":"약품 가공"}"""))
{
    Check(AlteringFacilityResolver.FromJson(metadataDoc.RootElement, "미지 제법") == "약품 가공 시설",
        "facility metadata is detected even when CLI key is not FacilityName");
}
using (var ingredientOnlyDoc = System.Text.Json.JsonDocument.Parse(
    """{"DisplayName":"미지 제법","Alterable":true,"ProducedPerWork":1,"Reason":null,"MissingIngredients":[{"DisplayName":"철괴","Required":1,"Owned":0}]}"""))
{
    Check(AlteringFacilityResolver.FromJson(ingredientOnlyDoc.RootElement, "미지 제법") is null,
        "ingredient names never misclassify an unknown recipe facility");
}
var statusNow = new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.FromHours(9));
var statusItems = new[]
{
    new AlteringStatusItem("metal", "철괴(철 광석)", 21, 100, 95, statusNow.AddSeconds(-5)),
    new AlteringStatusItem("wood", "목재", 50, 100, null, statusNow),
    new AlteringStatusItem("rice", "물에 불린 쌀", 100, 100, 0, statusNow)
};
string remoteStatus = AlteringStatusFormatter.Format(statusItems, 171, 300, statusNow);
Check(remoteStatus.Contains("전체 171/300 완료") &&
      remoteStatus.Contains("철괴(철 광석): 21/100 완료 · 남은시간(예상) 1분 30초") &&
      remoteStatus.Contains("목재: 50/100 완료 · 남은시간(예상) 계산 중") &&
      remoteStatus.Contains("물에 불린 쌀: 100/100 완료 · 남은시간(예상) 완료"),
    "processing status shows per-item completed/target counts and live remaining time");

Check(AlteringEtaEstimator.Estimate(
        requiredWorks: 10,
        queuedWorks: 4,
        activeItemSlots: 2,
        batchRemainingSeconds: 90,
        estimatedWorkSeconds: 100) == 390,
    "full-plan ETA includes the current batch plus future batches at the active slot share");
Check(AlteringEtaEstimator.Estimate(
        requiredWorks: 7,
        queuedWorks: 7,
        activeItemSlots: 3,
        batchRemainingSeconds: 45,
        estimatedWorkSeconds: 100) == 45,
    "full-plan ETA does not add future cycles after every work is already queued");

var estimatePlan = plan with { TargetQuantity = 10 };
var estimateRecipe = new AlteringRecipe("강철괴", false, 3, "not_enough_ingredient",
    new[] { new AlteringIngredient("철괴", 2, 3) }, plan.FacilityName);
string estimate = AlteringMaterialEstimate.Describe(estimatePlan, estimateRecipe);
Check(estimate.Contains("철괴 예상 8") && estimate.Contains("부족 약 5"),
    "material preview projects remaining-work ingredient shortage without inventing hidden materials");
Check(MultiAlteringMaterialPreflightPolicy.CanRun(
        hasResumableSession: false,
        hasSelectedFacilityWorks: false) &&
      !MultiAlteringMaterialPreflightPolicy.CanRun(
        hasResumableSession: true,
        hasSelectedFacilityWorks: false) &&
      !MultiAlteringMaterialPreflightPolicy.CanRun(
        hasResumableSession: false,
        hasSelectedFacilityWorks: true),
    "whole-plan material preflight runs only for a clean fresh multi-altering start");

var lanePlan = plan with { TargetQuantity = 3 };
var laneInitial = new[]
{
    new AlteringWork(lanePlan.OutputName, lanePlan.FacilityName, "InProgress", false, 10)
};
// M3: a fresh F9 session is location-unknown even with populated CLI works,
// resume JSON, or a previously confirmed lane from a different run.
var m3PriorRun = new FacilityLaneState(laneInitial);
m3PriorRun.ConfirmOnsite(lanePlan.FacilityName,
    "M3 prior run had independently verified the old facility");
var m3ColdResume = new FacilityLaneState(laneInitial);
var m3ColdLogs = new List<string>();
m3ColdResume.Log += m3ColdLogs.Add;
Check(m3PriorRun.IsOnsiteConfirmed(lanePlan.FacilityName) &&
      m3ColdResume.LocationProof == FacilityLocationProof.ColdStartUnknown &&
      m3ColdResume.QueueDirectiveFor(lanePlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      m3ColdLogs.Count(x => x.Contains("초기 위치 미확정(원격 확정 아님)")) == 1,
    "M3: F9 resume with existing works cannot inherit a prior-run onsite proof or claim the player is remote");
m3ColdResume.QueueDirectiveFor(lanePlan.FacilityName);
Check(m3ColdLogs.Count(x => x.Contains("초기 위치 미확정(원격 확정 아님)")) == 1,
    "M3: cold-start diagnostics are emitted once per facility rather than every round-robin turn");
m3ColdResume.Observe(lanePlan.FacilityName, laneInitial, allowShrink: false);
Check(m3ColdResume.LocationProof == FacilityLocationProof.ColdStartUnknown &&
      m3ColdResume.QueueDirectiveFor(lanePlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "M3: observing the same CLI seven-slot ledger is not physical location evidence");
m3ColdResume.ConfirmOnsite(lanePlan.FacilityName,
    "M3 current run verified physical arrival and CLI-confirmed registration");
Check(m3ColdResume.LocationProof == FacilityLocationProof.ConfirmedThisRun &&
      m3ColdResume.QueueDirectiveFor(lanePlan.FacilityName) ==
          AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite &&
      m3ColdResume.QueueDirectiveFor("목재 가공 시설") ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "M3: current-run onsite verification permits same-facility reuse, never a different facility");
m3ColdResume.InvalidateOnsite("M3 field exit or contradictory location evidence");
Check(m3ColdResume.LocationProof == FacilityLocationProof.RuntimeUncertain &&
      m3ColdResume.QueueDirectiveFor(lanePlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "M3: post-start location uncertainty is distinct from cold-start unknown and cannot reuse stale onsite");
Check(FacilityStartupLocationPolicy.DecideEntry(
          lanePlan.FacilityName, lanePlan.FacilityName,
          FacilityLocationProof.ColdStartUnknown) ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      FacilityStartupLocationPolicy.DecideEntry(
          lanePlan.FacilityName, lanePlan.FacilityName,
          FacilityLocationProof.RuntimeUncertain) ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "M3: even a stale same-name location cannot authorize reuse without current-run confirmation");

var laneOwnership = new FacilityLaneState(laneInitial);
Check(laneOwnership.Snapshot(lanePlan.FacilityName).InitialObservedWorks == 1,
    "facility ownership captures works that existed before automation start");
laneOwnership.NoteRegistration(lanePlan, FacilityLaneOwner.Main, 1);
laneOwnership.Observe(
    lanePlan.FacilityName,
    new[]
    {
        laneInitial[0],
        new AlteringWork(lanePlan.OutputName, lanePlan.FacilityName, "NotStarted", false, 20)
    },
    allowShrink: false);
Check(laneOwnership.Snapshot(lanePlan.FacilityName).MainRegisteredWorks == 1,
    "facility ownership records only confirmed main registrations");

Check(laneOwnership.QueueDirectiveFor(lanePlan.FacilityName) ==
      AlteringFacilityEntryDirective.FreshMoveRequired,
    "facility manager requires one fresh move before any onsite proof");
laneOwnership.ConfirmOnsite(
    lanePlan.FacilityName,
    "test receipt return");
Check(laneOwnership.QueueDirectiveFor(lanePlan.FacilityName) ==
      AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite &&
      laneOwnership.QueueDirectiveFor("목재 가공 시설") ==
      AlteringFacilityEntryDirective.FreshMoveRequired,
    "facility manager alone decides same-facility reuse versus a different-facility move");
laneOwnership.InvalidateOnsite("test field departure");
Check(laneOwnership.QueueDirectiveFor(lanePlan.FacilityName) ==
      AlteringFacilityEntryDirective.FreshMoveRequired,
    "facility manager invalidation returns the next registration to the fresh-move route");

var foreignGrowthOwnership = new FacilityLaneState(Array.Empty<AlteringWork>());
try
{
    foreignGrowthOwnership.Observe(
        lanePlan.FacilityName,
        new[] { new AlteringWork(lanePlan.OutputName, lanePlan.FacilityName, "InProgress", false, 10) },
        allowShrink: false);
    throw new Exception("unowned facility growth accepted");
}
catch (InvalidOperationException)
{
    Check(true, "facility ownership stops on a queue increase not explained by automation");
}

var foreignShrinkOwnership = new FacilityLaneState(laneInitial);
try
{
    foreignShrinkOwnership.Observe(
        lanePlan.FacilityName,
        Array.Empty<AlteringWork>(),
        allowShrink: false);
    throw new Exception("unowned facility shrink accepted");
}
catch (InvalidOperationException)
{
    Check(true, "facility ownership stops on manual receipt/cancel during a no-input wait");
}
foreignShrinkOwnership.Observe(
    lanePlan.FacilityName,
    Array.Empty<AlteringWork>(),
    allowShrink: true);
foreignShrinkOwnership.AcquireIntermediate(
    lanePlan.FacilityName,
    Array.Empty<AlteringWork>());
try
{
    foreignShrinkOwnership.AssertAccess(
        lanePlan.FacilityName,
        FacilityLaneOwner.Main);
    throw new Exception("main input accepted during intermediate ownership");
}
catch (InvalidOperationException)
{
    Check(true, "intermediate facility lease blocks main multi-altering input");
}
foreignShrinkOwnership.NoteRegistration(
    lanePlan,
    FacilityLaneOwner.Intermediate,
    1);
foreignShrinkOwnership.Observe(
    lanePlan.FacilityName,
    new[] { new AlteringWork(lanePlan.OutputName, lanePlan.FacilityName, "Completed", true, 0) },
    allowShrink: false);
try
{
    foreignShrinkOwnership.ReleaseIntermediate(
        lanePlan.FacilityName,
        new[] { new AlteringWork(lanePlan.OutputName, lanePlan.FacilityName, "Completed", true, 0) });
    throw new Exception("intermediate ownership released with live work");
}
catch (InvalidOperationException)
{
    Check(true, "intermediate facility lease is retained while its work remains");
}
foreignShrinkOwnership.Observe(
    lanePlan.FacilityName,
    Array.Empty<AlteringWork>(),
    allowShrink: true);
foreignShrinkOwnership.ReleaseIntermediate(
    lanePlan.FacilityName,
    Array.Empty<AlteringWork>());
var releasedLane = foreignShrinkOwnership.Snapshot(lanePlan.FacilityName);
Check(!releasedLane.IntermediateLease &&
      releasedLane.IntermediateRegisteredWorks == 1,
    "intermediate facility ownership releases only after the lane is empty");

var nestedLane = new FacilityLaneState(Array.Empty<AlteringWork>());
nestedLane.AcquireIntermediate(lanePlan.FacilityName, Array.Empty<AlteringWork>(), "parent#1");
Check(nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 1,
    "parent intermediate obtains sole facility lease");
nestedLane.NoteRegistration(lanePlan, FacilityLaneOwner.Intermediate, 1);
var nestedCompleted = new[]
{
    new AlteringWork(lanePlan.OutputName, lanePlan.FacilityName, "Completed", true, 0)
};
nestedLane.Observe(lanePlan.FacilityName, nestedCompleted, allowShrink: false);
try
{
    nestedLane.AcquireIntermediate(lanePlan.FacilityName, nestedCompleted, "child#1");
    throw new Exception("nested intermediate acquired a nonempty facility");
}
catch (InvalidOperationException)
{
    Check(nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 1,
        "child intermediate cannot borrow a facility before parent's batch is received");
}
nestedLane.Observe(lanePlan.FacilityName, Array.Empty<AlteringWork>(), allowShrink: true);
nestedLane.AcquireIntermediate(lanePlan.FacilityName, Array.Empty<AlteringWork>(), "child#1");
Check(nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 2,
    "same-facility nested intermediate borrows the empty lane without dropping parent lease");
try
{
    nestedLane.ReleaseIntermediate(lanePlan.FacilityName, Array.Empty<AlteringWork>(), "parent#1");
    throw new Exception("parent lease released before child");
}
catch (InvalidOperationException)
{
    Check(nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 2,
        "intermediate leases must release in child-first order");
}
try
{
    nestedLane.AcquireIntermediate(lanePlan.FacilityName, Array.Empty<AlteringWork>(), "parent#1");
    throw new Exception("duplicate active parent lease");
}
catch (InvalidOperationException)
{
    Check(nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 2,
        "duplicate/re-entrant owner keys never increase intermediate lease depth");
}
nestedLane.NoteRegistration(lanePlan, FacilityLaneOwner.Intermediate, 1);
nestedLane.Observe(lanePlan.FacilityName, nestedCompleted, allowShrink: false);
try
{
    nestedLane.ReleaseIntermediate(lanePlan.FacilityName, nestedCompleted, "child#1");
    throw new Exception("child lease released before collecting its work");
}
catch (InvalidOperationException)
{
    Check(nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 2,
        "nested child lease retains ownership while child work is live");
}
nestedLane.Observe(lanePlan.FacilityName, Array.Empty<AlteringWork>(), allowShrink: true);
nestedLane.ReleaseIntermediate(lanePlan.FacilityName, Array.Empty<AlteringWork>(), "child#1");
Check(nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 1,
    "child release restores parent's intermediate lease");
try
{
    nestedLane.AssertAccess(lanePlan.FacilityName, FacilityLaneOwner.Main);
    throw new Exception("main input accepted while parent lease remained");
}
catch (InvalidOperationException)
{
    Check(true, "main registration stays blocked while the parent intermediate owns the lane");
}
nestedLane.ReleaseIntermediate(lanePlan.FacilityName, Array.Empty<AlteringWork>(), "parent#1");
Check(!nestedLane.Snapshot(lanePlan.FacilityName).IntermediateLease &&
      nestedLane.Snapshot(lanePlan.FacilityName).IntermediateDepth == 0 &&
      nestedLane.Snapshot(lanePlan.FacilityName).IntermediateRegisteredWorks == 2,
    "last parent release returns the lane to main without losing nested work ownership counts");

string sessionPath = Path.Combine(Path.GetTempPath(), "mabi-altering-" + Guid.NewGuid().ToString("N") + ".json");
var sessionStore = new AlteringSessionStore(sessionPath);
var testIdentity = new CliIdentityContext("char-1", "테스트", "account-1", "서버A");
var persisted = AlteringSessionState.Create(plan, testIdentity, 123, 2) with
{
    QueuedWorks = 5,
    CreditedInternalConsumptionQuantity = 3,
    Stage = "재료 해결 · 철괴"
};
sessionStore.Save(persisted);
var loadedSession = sessionStore.Load();
Check(loadedSession is not null && loadedSession.MatchesPlan(plan) &&
      loadedSession.MatchesIdentity(testIdentity) &&
      loadedSession.QueuedWorks == 5 && loadedSession.BaselineQuantity == 123 &&
      loadedSession.LastObservedOutputQuantity == 123 &&
      loadedSession.CreditedInternalConsumptionQuantity == 3 &&
      loadedSession.Stage.Contains("철괴"),
    "altering session persists plan, identity, baseline, internal consumption credit, progress, and recursive stage");
sessionStore.Delete();
Check(!File.Exists(sessionPath), "completed session cleanup removes persisted resume state");
string multiKeyDir = Path.Combine(Path.GetTempPath(), "mabi-multi-key-test");
string stablePathA = AlteringSessionStore.MultiPlanPath(multiKeyDir, plan);
string stablePathARepeat = AlteringSessionStore.MultiPlanPath(multiKeyDir, plan);
string stablePathB = AlteringSessionStore.MultiPlanPath(
    multiKeyDir,
    plan with { DisplayName = "철괴(철 광석)" });
Check(stablePathA == stablePathARepeat &&
      stablePathA != stablePathB &&
      !AlteringSessionStore.IsLegacyMultiPath(stablePathA) &&
      AlteringSessionStore.IsLegacyMultiPath(Path.Combine(multiKeyDir, "00.json")),
    "multi-altering session path is stable by plan identity and independent of queue order");

var internalIronPlan = new AlteringPlan(
    "금속 가공 시설", "철괴(철 광석)", 10, 3, false);
var internalSteelPlan = new AlteringPlan(
    "금속 가공 시설", "강철괴", 10, 3, false);
string internalSessionPath = Path.Combine(
    Path.GetTempPath(), "mabi-internal-consumption-" + Guid.NewGuid().ToString("N") + ".json");
var internalStore = new AlteringSessionStore(internalSessionPath);
var internalSession = AlteringSessionState.Create(
    internalIronPlan, testIdentity, baseline: 239, initialExistingWorks: 0);
internalStore.Save(internalSession);
var internalWorld = new FakeWorld(internalIronPlan) { Owned = 236 };
var internalIronAutomation = new AlteringAutomation(
    internalWorld,
    internalWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    sessionStore: internalStore,
    session: internalSession);
var accountingInventory = new Dictionary<string, long>(StringComparer.Ordinal)
{
    ["철괴"] = 239,
    ["강철괴"] = 1519
};
var internalLedger = new MultiAlteringConsumptionLedger(
    (names, token) =>
    {
        token.ThrowIfCancellationRequested();
        IReadOnlyDictionary<string, long> snapshot = names.ToDictionary(
            name => name,
            name => accountingInventory[name],
            StringComparer.Ordinal);
        return Task.FromResult(snapshot);
    });
internalLedger.RegisterProducer(
    internalIronPlan,
    (quantity, consumer) =>
        internalIronAutomation.CreditInternalConsumption(quantity, consumer));
internalLedger.RegisterProducer(
    internalSteelPlan,
    (_, _) => throw new Exception("steel output should not be credited in this test"));

var beforeSteelRegistration = await internalLedger.CaptureBeforeRegistrationAsync(
    internalSteelPlan, default);
accountingInventory["철괴"] = 236;
await internalLedger.CommitAfterRegistrationAsync(
    internalSteelPlan, beforeSteelRegistration, default);

var creditedIronSession = internalStore.Load()
    ?? throw new Exception("internal consumption session missing");
Check(creditedIronSession.CreditedInternalConsumptionQuantity == 3,
    "steel registration credits the observed 239 to 236 iron decrease as internal consumption");

var restartedIronAutomation = new AlteringAutomation(
    internalWorld,
    internalWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    sessionStore: internalStore,
    session: creditedIronSession);
await restartedIronAutomation.RunBatchAsync(internalIronPlan, 1, default);
Check(internalWorld.QueueCalls == 1,
    "persisted internal consumption credit lets iron resume after steel consumed three iron");
internalStore.Delete();
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
Check(success.Automation.QueuedWorks == 34 &&
      success.Automation.ConfirmedRegistrationsThisRun == 34 &&
      success.World.QueueCalls == 34,
    "one registration per required work and ownership counts only confirmed inputs");
Check(success.World.Owned == 102 && success.World.MaxQueue <= 7, "full queue waits and all results collected");
Check(success.Automation.ReservedWings == 0, "successful altering reserves zero Spirit Wings");
success = await Run(plan with { DisplayName = "철괴(광석)", TargetQuantity = 2 });
Check(success.World.Owned == 3, "qualified recipe work and inventory verification");
success = await Run(plan with { TargetQuantity = 4 }, w => w.Bonus = 2);
Check(success.World.Owned == 10, "critical rewards can exceed minimum target");
success = await Run(plan with { TargetQuantity = 4 }, w => w.AddCompleted(3));
Check(success.World.Owned == 9 && success.World.SecondStageCalls == 0, "existing completed work is received without counting as a queued target work");
success = await Run(plan with { TargetQuantity = 4 }, w => { w.AddCompleted(3); w.TwoStageCollect = true; });
Check(success.World.Owned == 9 && success.World.SecondStageCalls > 0, "travel-first collect waits for second confirmed Space without duplicate receipt");
success = await Run(plan with { TargetQuantity = 10 }, w => { for(int i=0;i<7;i++) w.AddPending(waitingOnly:i>0); });
Check(success.World.QueueCalls == 4 && success.World.Owned == 33 && success.World.QueuedWhileExisting && success.Automation.ReservedWings == 0, "new target work fills a freed slot while older jobs still remain");
success = await Run(plan with { TargetQuantity = 4 }, w => { w.AddPending(); w.AddCompleted(3); w.Bonus=2; });
Check(success.World.QueueCalls == 2 && success.World.Owned == 20, "mixed completed and pending jobs with critical rewards excluded from new target");
var waiting = new FakeWorld(plan) { Freeze = true };
for (int i = 0; i < 7; i++) waiting.AddPending(waitingOnly: i > 0);
using(var cts = new CancellationTokenSource())
{
    var waitingAuto = new AlteringAutomation(waiting, waiting, (_, token) => { cts.Cancel(); token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, 2);
    try { await waitingAuto.RunAsync(plan, cts.Token); throw new Exception("full existing queue wait ignored cancellation"); }
    catch(OperationCanceledException) { Check(waiting.QueueCalls == 0 && waitingAuto.ReservedWings == 0, "stop during full existing-work wait never spends currency"); }
}
var stalled = new FakeWorld(plan) { Freeze = true };
for (int i = 0; i < 7; i++) stalled.AddPending(waitingOnly:true);
try { await new AlteringAutomation(stalled, stalled, (_,_) => Task.CompletedTask, 2).RunAsync(plan, default); throw new Exception("stalled full existing queue ignored"); }
catch(InvalidOperationException) { Check(stalled.QueueCalls == 0, "stalled full existing queue stops without paid input"); }
var missing = new FakeWorld(plan) { Available = false };
try { await new AlteringAutomation(missing, missing).RunAsync(plan, default); throw new Exception("missing materials accepted without resolver"); }
catch (InvalidOperationException) { Check(missing.QueueCalls == 0, "missing materials without resolver stop before any registration"); }
var resolvedWorld = new FakeWorld(plan with { TargetQuantity = 3 }) { Available = false };
var resolver = new FakeResolver(resolvedWorld);
var resolvedAuto = new AlteringAutomation(resolvedWorld, resolvedWorld, (_, _) => Task.CompletedTask, 4, resolver);
await resolvedAuto.RunAsync(plan with { TargetQuantity = 3 }, default);
Check(resolver.Calls == 1 && resolvedWorld.QueueCalls == 1 && resolvedAuto.ReservedWings == 0,
    "missing materials are resolved inside the same zero-wing altering session");

// A partial raw stock of 100 is NOT permission to reject a 200-output
// target before its first valid work ticket. Later tickets are gated by the
// same live recipe availability check as the very first ticket.
var partialLeather = new AlteringPlan("가죽 가공 시설", "가죽+", 200, 3, false);
var partialLeatherWorld = new FakeWorld(partialLeather) { RawLeatherStock = 100, LeatherCost = 2 };
var firstLeatherStartLogs = new List<string>();
var partialStart = await AlteringQueueStartGuard.EnsureStartableWithRefreshAsync(
    new[] { partialLeather }, partialLeatherWorld.RecipesAsync,
    partialLeatherWorld.WorksAsync,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    CancellationToken.None, firstLeatherStartLogs.Add);
Check(firstLeatherStartLogs.Any(x =>
        x.Contains("가죽+", StringComparison.Ordinal) &&
        x.Contains("1회 제작 가능", StringComparison.Ordinal) &&
        x.Contains("목표 200개", StringComparison.Ordinal)),
    "F9 logs first-work availability for leather+ 200 target with only 100 raw stock");
var partialLeatherAuto = new AlteringAutomation(
    partialLeatherWorld, partialLeatherWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; }, 4);
var partialFirstBatch = await partialLeatherAuto.RunBatchAsync(partialLeather, 1, default);
Check(partialStart.Recipes.Single().Alterable &&
      partialFirstBatch == AlteringRunResult.BatchQueued &&
      partialLeatherWorld.QueueCalls == 1 &&
      partialLeatherWorld.RawLeatherStock == 98 &&
      partialLeatherAuto.ReservedWings == 0,
    "leather+ 200 target registers first free work with 100 raw stock; no full-target material gate");

// Read-only leather route inspection must follow a known craftable
// intermediary but HOLD on an externally supplied hide shortage.
var leatherRoutePlan = new AlteringPlan("가죽 가공 시설", "가죽+", 3, 3, false);
var leatherRouteRecipe = new AlteringRecipe("가죽+", false, 3,
    "not_enough_ingredient", [new("가죽", 3, 0)], "가죽 가공 시설");
var leatherProducer = new AlteringRecipe("가죽", false, 3,
    "not_enough_ingredient", [new("상급 생가죽", 10, 0)], "가죽 가공 시설");
var leatherRouteRecipes = new[] { leatherRouteRecipe, leatherProducer };
var leatherRouteStop = await MaterialSupplyRoutePolicy.FindHoldAsync(
    leatherRoutePlan, leatherRouteRecipe, leatherRouteRecipes,
    Array.Empty<GatherableItem>(),
    (name, ct) => Task.FromResult(0L), CancellationToken.None);
Check(leatherRouteStop is not null &&
      leatherRouteStop.Item == "상급 생가죽" &&
      leatherRouteStop.Observed == 0 &&
      leatherRouteStop.Required == 10 &&
      leatherRouteStop.Category == "external_leather_raw_missing",
    "read-only leather inspection follows producer and holds missing higher raw hide");
var leatherRouteReady = await MaterialSupplyRoutePolicy.FindHoldAsync(
    leatherRoutePlan, leatherRouteRecipe, leatherRouteRecipes,
    Array.Empty<GatherableItem>(),
    (name, ct) => Task.FromResult(name == "상급 생가죽" ? 10L : 0L),
    CancellationToken.None);
Check(leatherRouteReady is not null &&
      leatherRouteReady.Item == "상급 생가죽" &&
      leatherRouteReady.Category == "leather_recipe_inventory_mismatch" &&
      leatherRouteReady.Observed == 10,
    "stale recipe Owned=0 vs get_items=10 must HOLD, not mislabel as raw exhaustion");
var partialHideRecipe = new AlteringRecipe("가죽", false, 3,
    "not_enough_ingredient", [new("생가죽", 10, 6)], "가죽 가공 시설");
var partialHideHold = await MaterialSupplyRoutePolicy.FindHoldAsync(
    new AlteringPlan("가죽 가공 시설", "가죽", 3, 3, false),
    partialHideRecipe, [partialHideRecipe], Array.Empty<GatherableItem>(),
    (name, token) => Task.FromResult(6L), CancellationToken.None);
Check(partialHideHold?.Category == "external_leather_raw_missing" &&
      partialHideHold.Observed == 6 && partialHideHold.Required == 10,
    "6 raw hides in inventory are insufficient for one 10-hide ticket, not zero hides");
var conflictingHideHold = await MaterialSupplyRoutePolicy.FindHoldAsync(
    new AlteringPlan("가죽 가공 시설", "가죽", 3, 3, false),
    partialHideRecipe, [partialHideRecipe], Array.Empty<GatherableItem>(),
    (name, token) => Task.FromResult(0L), CancellationToken.None);
Check(conflictingHideHold?.Category == "leather_recipe_inventory_mismatch" &&
      conflictingHideHold.Observed == 0,
    "recipe-owned 6 but parsed bag 0 never proves raw hide exhaustion");
var tanninRouteRecipe = new AlteringRecipe("가죽+", false, 3,
    "not_enough_ingredient", [new("타닌 가루", 2, 0)], "가죽 가공 시설");
var tanninHold = await MaterialSupplyRoutePolicy.FindHoldAsync(
    leatherRoutePlan, tanninRouteRecipe, [tanninRouteRecipe],
    Array.Empty<GatherableItem>(),
    (name, ct) => Task.FromResult(0L), CancellationToken.None);
Check(tanninHold?.Category == "external_leather_raw_missing" &&
      tanninHold.Item == "타닌 가루" && tanninHold.Required == 2,
    "tannin shortage pauses leather before any automatic acquisition");

// Craftable 가죽 is a valid recursive ingredient, not a terminal shortage.
var craftableLeather = new AlteringPlan("가죽 가공 시설", "가죽+", 3, 3, false);
var craftableLeatherWorld = new FakeWorld(craftableLeather)
    { Available = false, MissingIngredient = "가죽" };
var craftableLeatherResolver = new FakeResolver(craftableLeatherWorld);
var craftableLeatherAuto = new AlteringAutomation(
    craftableLeatherWorld, craftableLeatherWorld, (_, _) => Task.CompletedTask,
    4, craftableLeatherResolver);
var craftableLeatherResult = await craftableLeatherAuto.RunBatchAsync(
    craftableLeather, 1, default);
Check(craftableLeatherResult == AlteringRunResult.BatchQueued &&
      craftableLeatherResolver.Calls == 1 && craftableLeatherWorld.QueueCalls == 1 &&
      craftableLeatherAuto.ReservedWings == 0,
    "craftable leather shortage invokes recursive resolver and registers first free ticket");

// Nested raw shortage must be a normal leather-item terminal, not an
// unhandled exception that stops all independent facility workers.
var nestedRawWorld = new FakeWorld(craftableLeather)
    { Available = false, MissingIngredient = "가죽" };
var nestedRawResolver = new FakeResolver(nestedRawWorld)
    { MissingExternalRaw = "상급 생가죽" };
var nestedRawAuto = new AlteringAutomation(
    nestedRawWorld, nestedRawWorld, (_, ct) => Task.CompletedTask, 4,
    nestedRawResolver);
var nestedRawResult = await nestedRawAuto.RunAsync(craftableLeather, default);
Check(nestedRawResult == AlteringRunResult.RawMaterialExhausted &&
      nestedRawResolver.Calls == 1 &&
      nestedRawWorld.QueueCalls == 0 &&
      nestedRawAuto.ReservedWings == 0,
    "missing nested raw hide ends only leather recipe before input with zero wings");

var rawGuardWorld = new RecursiveProductionWorld();
var rawGuardResolver = new RecursiveAlteringSupplyResolver(
    rawGuardWorld, rawGuardWorld, rawGuardWorld, rawGuardWorld);
bool rawGuardRejected = false;
try
{
    await rawGuardResolver.ResolveExternalAsync("타닌 가루", 2, default);
}
catch (MaterialSupplyHoldException ex)
{
    rawGuardRejected = ex.Evidence.Item == "타닌 가루" &&
        ex.Evidence.Required == 2 && ex.Evidence.Observed == 0 &&
        ex.Evidence.Category == "external_leather_raw_missing";
}
Check(rawGuardRejected &&
      rawGuardWorld.GatherStarts == 0 &&
      rawGuardWorld.FieldExitCalls == 0 &&
      rawGuardWorld.Queued.Count == 0,
    "direct resolver entry refuses tannin gathering/crafting before any input");

// Integration: real recursive resolver (not a fake bypass) crafts ordinary
// leather first and then queues leather+ without gathering raw hide or tannin.
var leatherChainWorld = new RecursiveProductionWorld { IncludeLeather = true };
leatherChainWorld.SetCount("생가죽", 10);
leatherChainWorld.SetCount("타닌 가루", 4);
var leatherChainResolver = new RecursiveAlteringSupplyResolver(
    leatherChainWorld, leatherChainWorld, leatherChainWorld, leatherChainWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
var leatherChainPlan = new AlteringPlan("가죽 가공 시설", "가죽+", 3, 3, false);
var leatherChainAuto = new AlteringAutomation(
    leatherChainWorld, leatherChainWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, leatherChainResolver);
var leatherChainResult = await leatherChainAuto.RunBatchAsync(
    leatherChainPlan, 1, default);
Check(leatherChainResult == AlteringRunResult.BatchQueued &&
      leatherChainWorld.Queued.SequenceEqual(new[] { "가죽", "가죽+" }) &&
      leatherChainWorld.Count("가죽") == 0 &&
      leatherChainWorld.Count("생가죽") == 0 &&
      leatherChainWorld.Count("타닌 가루") == 0 &&
      leatherChainWorld.GatherStarts == 0 &&
      leatherChainWorld.FieldExitCalls == 0 &&
      leatherChainAuto.ReservedWings == 0,
    "recursive leather chain crafts intermediate before parent without raw gathering or wings");

// Customer-reported raw inventory: 500 생가죽 but zero ordinary 가죽.
// The 200-target must start one verified leather ticket instead of treating
// the final target quantity or missing intermediate as a raw exhaustion.
var fiveHundredHides = new RecursiveProductionWorld { IncludeLeather = true };
fiveHundredHides.SetCount("생가죽", 500);
fiveHundredHides.SetCount("타닌 가루", 4);
var fiveHundredResolver = new RecursiveAlteringSupplyResolver(
    fiveHundredHides, fiveHundredHides, fiveHundredHides, fiveHundredHides,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
var fiveHundredPlan = new AlteringPlan("가죽 가공 시설", "가죽+", 200, 3, false);
var fiveHundredAuto = new AlteringAutomation(
    fiveHundredHides, fiveHundredHides,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, fiveHundredResolver);
var first500RawBatch = await fiveHundredAuto.RunBatchAsync(
    fiveHundredPlan, 1, default);
Check(first500RawBatch == AlteringRunResult.BatchQueued &&
      fiveHundredHides.Queued.Count(x => x == "가죽") == 7 &&
      fiveHundredHides.Queued.Count(x => x == "가죽+") == 1 &&
      fiveHundredHides.Count("생가죽") == 430 &&
      fiveHundredHides.GatherStarts == 0 &&
      fiveHundredAuto.ReservedWings == 0,
    "500 hides + one ticket of tannin allows seven leather intermediates and the first parent work");

// With adequate confirmed stock, one F9 pass fills seven dependency
// tickets, receives the completed batch, and fills seven parent tickets.
// Recipe quantities here are synthetic fixtures, NOT live CLI recipe claims.
var leatherSevenWorld = new RecursiveProductionWorld { IncludeLeather = true };
leatherSevenWorld.SetCount("생가죽", 500);
leatherSevenWorld.SetCount("타닌 가루", 28);
var leatherSevenResolver = new RecursiveAlteringSupplyResolver(
    leatherSevenWorld, leatherSevenWorld, leatherSevenWorld, leatherSevenWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
var leatherSevenPlan = new AlteringPlan("가죽 가공 시설", "가죽+", 200, 3, false);
var leatherSevenAuto = new AlteringAutomation(
    leatherSevenWorld, leatherSevenWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, leatherSevenResolver);
var leatherSevenResult = await leatherSevenAuto.RunBatchAsync(
    leatherSevenPlan, 7, default);
Check(leatherSevenResult == AlteringRunResult.BatchQueued &&
      leatherSevenWorld.Queued.Count(x => x == "가죽") == 7 &&
      leatherSevenWorld.Queued.Count(x => x == "가죽+") == 7 &&
      leatherSevenWorld.Queued.Take(7).All(x => x == "가죽") &&
      leatherSevenWorld.Queued.Skip(7).Take(7).All(x => x == "가죽+") &&
      leatherSevenWorld.Count("생가죽") == 430 &&
      leatherSevenWorld.Count("타닌 가루") == 0 &&
      leatherSevenWorld.GatherStarts == 0 &&
      leatherSevenAuto.ReservedWings == 0,
    "500 hides: leather seven-slot child batch and seven-slot parent batch, no gathering or wings");

// Insufficient raw stock for a full seven-slot batch must not prevent the
// first three safe tickets. Once hides run low, child receipts can satisfy
// the next parent ticket without inventing the missing fourth input.
var leatherPartialWorld = new RecursiveProductionWorld { IncludeLeather = true };
leatherPartialWorld.SetCount("생가죽", 35);
leatherPartialWorld.SetCount("타닌 가루", 28);
var leatherPartialResolver = new RecursiveAlteringSupplyResolver(
    leatherPartialWorld, leatherPartialWorld, leatherPartialWorld, leatherPartialWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
var leatherPartialAuto = new AlteringAutomation(
    leatherPartialWorld, leatherPartialWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, leatherPartialResolver);
var leatherPartialResult = await leatherPartialAuto.RunBatchAsync(
    leatherSevenPlan, 7, default);
Check(leatherPartialResult == AlteringRunResult.BatchQueued &&
      leatherPartialWorld.Queued.Count(x => x == "가죽") == 3 &&
      leatherPartialWorld.Queued.Count(x => x == "가죽+") == 3 &&
      leatherPartialWorld.Count("생가죽") == 5 &&
      leatherPartialWorld.GatherStarts == 0 &&
      leatherPartialAuto.ReservedWings == 0,
    "35 hides produce three leather and three leather+ tickets safely; remaining raw short does not block partial receipt");

// Synthetic full chain: upper leather -> leather+ -> leather. The
// fixture's recipe costs are test data, not a statement about live CLI.
var upperLeatherWorld = new RecursiveProductionWorld { IncludeLeather = true };
upperLeatherWorld.SetCount("생가죽", 10);
upperLeatherWorld.SetCount("상급 생가죽", 15);
upperLeatherWorld.SetCount("타닌 가루", 12);
var upperLeatherResolver = new RecursiveAlteringSupplyResolver(
    upperLeatherWorld, upperLeatherWorld, upperLeatherWorld, upperLeatherWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
var upperLeatherPlan = new AlteringPlan("가죽 가공 시설", "상급 가죽", 3, 3, false);
var upperLeatherAuto = new AlteringAutomation(
    upperLeatherWorld, upperLeatherWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, upperLeatherResolver);
var upperLeatherResult = await upperLeatherAuto.RunBatchAsync(
    upperLeatherPlan, 1, default);
Check(upperLeatherResult == AlteringRunResult.BatchQueued &&
      upperLeatherWorld.Queued.SequenceEqual(new[] { "가죽", "가죽+", "상급 가죽" }) &&
      upperLeatherWorld.Count("생가죽") == 0 &&
      upperLeatherWorld.Count("상급 생가죽") == 0 &&
      upperLeatherWorld.Count("타닌 가루") == 0 &&
      upperLeatherWorld.GatherStarts == 0 &&
      upperLeatherAuto.ReservedWings == 0,
    "upper leather crafts leather then leather+ before upper recipe with no raw gathering or wings");

// A missing external input anywhere in that recursive chain must hold the
// parent before production begins, rather than starting intermediate tickets.
var upperLeatherHold = await MaterialSupplyRoutePolicy.FindHoldAsync(
    upperLeatherPlan,
    (await upperLeatherWorld.RecipesAsync(default)).Single(x => x.DisplayName == "상급 가죽"),
    await upperLeatherWorld.RecipesAsync(default),
    Array.Empty<GatherableItem>(),
    (name, token) => Task.FromResult(
        name == "상급 생가죽" ? 0L : 100L),
    default);
Check(upperLeatherHold is not null &&
      upperLeatherHold.Item == "상급 생가죽" &&
      upperLeatherHold.Category == "external_leather_raw_missing",
    "upper leather read-only preflight refuses missing upper raw hide");

// Only hide-tier raw materials and tannin stop a leather recipe.
var exhaustedLeather = new AlteringPlan("가죽 가공 시설", "가죽+", 9, 3, false);
var exhaustedWorld = new FakeWorld(exhaustedLeather)
    { Available = false, MissingIngredient = "생가죽" };
var exhaustedResolver = new FakeResolver(exhaustedWorld);
var exhaustedAuto = new AlteringAutomation(
    exhaustedWorld, exhaustedWorld, (_, _) => Task.CompletedTask,
    4, exhaustedResolver);
var exhaustedResult = await exhaustedAuto.RunAsync(exhaustedLeather, default);
Check(exhaustedResult == AlteringRunResult.RawMaterialExhausted &&
      exhaustedWorld.QueueCalls == 0 && exhaustedResolver.Calls == 0 &&
      exhaustedAuto.ReservedWings == 0,
    "raw leather shortage terminates this recipe without replenishment, registration, or wings");
var partialRawWorld = new FakeWorld(exhaustedLeather) {
    Available = false, MissingIngredient = "생가죽",
    MissingIngredientOwned = 2, MissingIngredientRequired = 3,
    RawObservedOverride = 2
};
var partialRawLogs = new List<string>();
var partialRawAuto = new AlteringAutomation(
    partialRawWorld, partialRawWorld, (_, _) => Task.CompletedTask, 4);
partialRawAuto.Log += partialRawLogs.Add;
var partialRawResult = await partialRawAuto.RunAsync(exhaustedLeather, default);
Check(partialRawResult == AlteringRunResult.RawMaterialExhausted &&
      partialRawLogs.Any(x => x.Contains("생가죽 2/3", StringComparison.Ordinal)) &&
      partialRawLogs.Any(x => x.Contains("원재료 부족", StringComparison.Ordinal)) &&
      partialRawWorld.QueueCalls == 0,
    "positive raw hide stock below one-ticket requirement reports 2/3 shortage, not empty stock");
var conflictingRawWorld = new FakeWorld(exhaustedLeather) {
    Available = false, MissingIngredient = "생가죽",
    MissingIngredientOwned = 2, MissingIngredientRequired = 3,
    RawObservedOverride = 10
};
var conflictingRawAuto = new AlteringAutomation(
    conflictingRawWorld, conflictingRawWorld, (_, _) => Task.CompletedTask, 4);
bool heldConflict = false;
try { await conflictingRawAuto.RunAsync(exhaustedLeather, default); }
catch (MaterialSupplyHoldException ex) {
    heldConflict = ex.Evidence.Category == "leather_recipe_inventory_mismatch" &&
        ex.Evidence.Item == "생가죽" && ex.Evidence.Observed == 10;
}
Check(heldConflict && conflictingRawWorld.QueueCalls == 0 &&
      conflictingRawAuto.ReservedWings == 0,
    "recipe says 2/3 but inventory says 10: no durable raw-exhausted decision and no input");

Check(!LeatherRawMaterialPolicy.EndsOnShortage(exhaustedLeather,
          new AlteringRecipe("가죽+", false, 3, "short", [new("가죽", 1, 0)], "가죽 가공 시설")) &&
      LeatherRawMaterialPolicy.EndsOnShortage(exhaustedLeather,
          new AlteringRecipe("가죽+", false, 3, "short", [new("상급 생가죽", 1, 0)], "가죽 가공 시설")) &&
      LeatherRawMaterialPolicy.EndsOnShortage(exhaustedLeather,
          new AlteringRecipe("가죽+", false, 3, "short", [new("타닌 가루", 1, 0)], "가죽 가공 시설")) &&
      LeatherRawMaterialPolicy.IsExternalRaw("생가죽") &&
      LeatherRawMaterialPolicy.IsExternalRaw("상급 생가죽") &&
      LeatherRawMaterialPolicy.IsExternalRaw("타닌가루") &&
      !LeatherRawMaterialPolicy.IsExternalRaw("가죽") &&
      !LeatherRawMaterialPolicy.IsExternalRaw("가죽+") &&
      !LeatherRawMaterialPolicy.EndsOnShortage(plan,
          new AlteringRecipe(plan.DisplayName, false, 3, "short", [new("철 광석", 1, 0)], plan.FacilityName)) &&
      MultiAlteringItemStatePolicy.IsTerminal(MultiAlteringItemState.RawMaterialExhausted) &&
      !MultiAlteringItemStatePolicy.IsTerminal(MultiAlteringItemState.InProgress),
    "only missing external hides/tannin stop leather; intermediate leather remains craftable");

var oldLeatherWork = new FakeWorld(exhaustedLeather)
    { Available = false, MissingIngredient = "생가죽" };
oldLeatherWork.AddPending();
var oldLeatherResult = await new AlteringAutomation(
    oldLeatherWork, oldLeatherWork, (_, _) => Task.CompletedTask, 4)
    .RunAsync(exhaustedLeather, default);
Check(oldLeatherResult == AlteringRunResult.RawMaterialExhausted &&
      oldLeatherWork.QueueCalls == 0 && oldLeatherWork.CollectionCount == 1 &&
      oldLeatherWork.Owned == 3,
    "old leather jobs finish and are collected normally without starting any new jobs");

// Live 2026-10-18 17:55:24 V3.2.25 incident:
// Existing leather 6/7, all six Completed, previously saved raw-shortage
// flag. The initial-empty-slot exemption incorrectly prevented receipt,
// then the 120-second watchdog stopped all F9 work after 495 seconds.
// Exercise the ACTUAL managed RunBatch receipt and saved resume checkpoint,
// including exactly-one receipt, no seventh registration and no wings.
var sixLeatherComplete = new FakeWorld(exhaustedLeather)
    { Available = false, MissingIngredient = "생가죽", Freeze = true };
for (int i = 0; i < 6; i++) sixLeatherComplete.AddCompleted(3);
var sixLeatherSnapshot = await sixLeatherComplete.WorksAsync(default);
var sixLeatherLane = new FacilityLaneState(sixLeatherSnapshot);
string leatherCheckpointPath = Path.Combine(Path.GetTempPath(),
    "MabiAuto-leather-receipt-test-" + Guid.NewGuid().ToString("N"), "plan.json");
Directory.CreateDirectory(Path.GetDirectoryName(leatherCheckpointPath)!);
try
{
    var leatherCheckpointStore = new AlteringSessionStore(leatherCheckpointPath);
    var leatherCheckpoint = AlteringSessionState.Create(
        exhaustedLeather, new CliIdentityContext(null, null, null, null), 0, 6)
        with
        {
            BatchId = "regression-six-leather-complete",
            RawMaterialExhausted = true,
            ReceiptTrackingEnabled = true,
            MultiState = MultiAlteringItemState.InProgress,
            Stage = "가죽 원재료 소진 · 신규 등록 중단"
        };
    leatherCheckpointStore.Save(leatherCheckpoint);
    int guardedPreReceipt = 0, guardedPostReceipt = 0;
    var resumedLeatherAuto = new AlteringAutomation(
        sixLeatherComplete, sixLeatherComplete, (_, ct) => Task.CompletedTask,
        4, sessionStore: leatherCheckpointStore, session: leatherCheckpoint,
        facilityState: sixLeatherLane,
        beforeReceipt: async (p, ct) =>
        {
            var before = (await sixLeatherComplete.WorksAsync(ct))
                .Where(w => w.FacilityName == p.FacilityName).ToArray();
            if (before.Length != 6 || before.Any(w => !w.IsCompleted))
                throw new InvalidOperationException("test pre-receipt evidence lost");
            guardedPreReceipt++;
        },
        afterReceipt: async (p, ct) =>
        {
            var after = await sixLeatherComplete.WorksAsync(ct);
            if (after.Any(w => w.FacilityName == p.FacilityName))
                throw new InvalidOperationException("test receipt not fully confirmed");
            var latest = leatherCheckpointStore.Load()!;
            leatherCheckpointStore.Save(latest with
            {
                ConfirmedReceivedWorks = 6,
                LastObservedOutputQuantity = await sixLeatherComplete.ItemCountAsync(
                    p.OutputName, ct)
            });
            sixLeatherLane.Observe(p.FacilityName, after, allowShrink: true);
            guardedPostReceipt++;
        });
    var drainedLeatherResult = await resumedLeatherAuto.RunBatchAsync(
        exhaustedLeather, 1, default);
    var drainedCheckpoint = leatherCheckpointStore.Load()!;
    Check(drainedLeatherResult == AlteringRunResult.RawMaterialExhausted &&
          drainedCheckpoint.MultiState == MultiAlteringItemState.RawMaterialExhausted &&
          drainedCheckpoint.RawMaterialExhausted &&
          drainedCheckpoint.ConfirmedReceivedWorks == 6 &&
          sixLeatherComplete.CollectionCount == 1 &&
          sixLeatherComplete.ReceiptDirectives.Count == 1 &&
          sixLeatherComplete.QueueCalls == 0 &&
          sixLeatherComplete.Owned == 18 &&
          resumedLeatherAuto.ReservedWings == 0 &&
          guardedPreReceipt == 1 && guardedPostReceipt == 1,
        "V3.2.26: exhausted six-completed leather jobs are collected exactly once using managed receipt + durable checkpoint");
    var resumedAgain = await resumedLeatherAuto.RunBatchAsync(
        exhaustedLeather, 1, default);
    Check(resumedAgain == AlteringRunResult.RawMaterialExhausted &&
          sixLeatherComplete.CollectionCount == 1 &&
          sixLeatherComplete.QueueCalls == 0 && guardedPostReceipt == 1,
        "V3.2.26: terminal exhausted leather checkpoint cannot recollect or register on retry");
}
finally
{
    Directory.Delete(Path.GetDirectoryName(leatherCheckpointPath)!, recursive: true);
}

// Five completed plus one running is NOT a valid managed receive-all,
// even when leather is exhausted. Preserve the unfinished server job.
var leatherMixedWorld = new FakeWorld(exhaustedLeather)
    { Available = false, MissingIngredient = "생가죽", Freeze = true };
for (int i = 0; i < 5; i++) leatherMixedWorld.AddCompleted(3);
leatherMixedWorld.AddPending();
var leatherMixedCheckpoint = AlteringSessionState.Create(
    exhaustedLeather, new CliIdentityContext(null, null, null, null), 0, 6)
    with { BatchId = "regression-mixed-leather", RawMaterialExhausted = true,
        MultiState = MultiAlteringItemState.InProgress };
string leatherMixedPath = Path.Combine(Path.GetTempPath(),
    "MabiAuto-leather-mixed-test-" + Guid.NewGuid().ToString("N"), "plan.json");
Directory.CreateDirectory(Path.GetDirectoryName(leatherMixedPath)!);
try
{
    var mixedStore = new AlteringSessionStore(leatherMixedPath);
    mixedStore.Save(leatherMixedCheckpoint);
    var mixedAuto = new AlteringAutomation(
        leatherMixedWorld, leatherMixedWorld, (_, ct) => Task.CompletedTask,
        4, sessionStore: mixedStore, session: leatherMixedCheckpoint,
        facilityState: new FacilityLaneState(await leatherMixedWorld.WorksAsync(default)));
    var mixedResult = await mixedAuto.RunBatchAsync(exhaustedLeather, 1, default);
    Check(mixedResult == AlteringRunResult.BatchQueued &&
          leatherMixedWorld.CollectionCount == 0 &&
          leatherMixedWorld.QueueCalls == 0 &&
          (await leatherMixedWorld.WorksAsync(default)).Count == 6,
        "V3.2.26: exhausted partial-completion leather facility cannot trigger receive-all or add new work");
}
finally
{
    Directory.Delete(Path.GetDirectoryName(leatherMixedPath)!, recursive: true);
}

var noDeparturePlan = plan with { TargetQuantity = 3 };
var m1NoDepartureWorld = new FakeWorld(noDeparturePlan) { Available = false };
var m1NoDepartureLane = new FacilityLaneState(Array.Empty<AlteringWork>());
m1NoDepartureLane.ConfirmOnsite(noDeparturePlan.FacilityName,
    "M1 test: verified onsite before material-only resolution");
var m1NoDepartureResolver = new FakeResolver(m1NoDepartureWorld);
var noDepartureAuto = new AlteringAutomation(
    m1NoDepartureWorld, m1NoDepartureWorld, (_, _) => Task.CompletedTask,
    4, m1NoDepartureResolver, facilityState: m1NoDepartureLane);
await noDepartureAuto.RunAsync(noDeparturePlan, default);
Check(m1NoDepartureResolver.Calls == 1 &&
      m1NoDepartureWorld.Directives.SequenceEqual(
          new[] { AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite }) &&
      m1NoDepartureLane.IsOnsiteConfirmed(noDeparturePlan.FacilityName) &&
      m1NoDepartureWorld.QueueCalls == 1,
    "M1: material inspection and same-facility resolution without travel retain manager onsite through next registration");

var noInitialOnsiteWorld = new FakeWorld(noDeparturePlan) { Available = false };
var noInitialOnsiteLane = new FacilityLaneState(Array.Empty<AlteringWork>());
var noInitialOnsiteResolver = new FakeResolver(noInitialOnsiteWorld);
await new AlteringAutomation(
    noInitialOnsiteWorld, noInitialOnsiteWorld, (_, _) => Task.CompletedTask,
    4, noInitialOnsiteResolver, facilityState: noInitialOnsiteLane)
    .RunAsync(noDeparturePlan, default);
Check(noInitialOnsiteWorld.Directives.SequenceEqual(
          new[] { AlteringFacilityEntryDirective.FreshMoveRequired }) &&
      noInitialOnsiteResolver.Calls == 1,
    "M1: material inspection cannot invent onsite proof when the manager had no confirmed location");

var noDepartureFailureWorld = new FakeWorld(noDeparturePlan) { Available = false };
var noDepartureFailureLane = new FacilityLaneState(Array.Empty<AlteringWork>());
noDepartureFailureLane.ConfirmOnsite(noDeparturePlan.FacilityName,
    "M1 test: already onsite before material-only validation failure");
var noDepartureFailureResolver = new FakeResolver(noDepartureFailureWorld)
{
    FailBeforeLeaving = true
};
bool materialOnlyFailure = false;
try
{
    await new AlteringAutomation(
        noDepartureFailureWorld, noDepartureFailureWorld, (_, _) => Task.CompletedTask,
        4, noDepartureFailureResolver, facilityState: noDepartureFailureLane)
        .RunAsync(noDeparturePlan, default);
}
catch (InvalidOperationException ex)
{
    materialOnlyFailure = ex.Message.Contains("M1 simulated material validation failure");
}
Check(materialOnlyFailure &&
      noDepartureFailureResolver.Calls == 1 &&
      noDepartureFailureWorld.QueueCalls == 0 &&
      noDepartureFailureLane.IsOnsiteConfirmed(noDeparturePlan.FacilityName),
    "M1: material-only failure stops without queueing or revoking proven onsite when no departure occurred");


var changedReasonWorld = new FakeWorld(plan with { TargetQuantity = 3 })
{
    Available = false,
    MissingReason = "material_shortage_changed"
};
var changedReasonResolver = new FakeResolver(changedReasonWorld);
var changedReasonAuto = new AlteringAutomation(
    changedReasonWorld, changedReasonWorld, (_, _) => Task.CompletedTask, 4, changedReasonResolver);
await changedReasonAuto.RunAsync(plan with { TargetQuantity = 3 }, default);
Check(changedReasonResolver.Calls == 1 && changedReasonWorld.QueueCalls == 1,
    "concrete MissingIngredients resolve even when CLI reason text changes");
var resumePlan = plan with { TargetQuantity = 3 };
var resumeWorld = new FakeWorld(resumePlan);
resumeWorld.AddPending();
string resumePath = Path.Combine(Path.GetTempPath(), "mabi-altering-resume-" + Guid.NewGuid().ToString("N") + ".json");
var resumeStore = new AlteringSessionStore(resumePath);
var resumeSession = AlteringSessionState.Create(resumePlan, testIdentity, 0, 0) with
{
    PendingRegistration = true,
    PendingBeforeMatchingCount = 0,
    Stage = "작업 등록 확인"
};
resumeStore.Save(resumeSession);
var resumeAuto = new AlteringAutomation(
    resumeWorld, resumeWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, sessionStore: resumeStore, session: resumeSession);
await resumeAuto.RunAsync(resumePlan, default);
Check(resumeAuto.QueuedWorks == 1 && resumeWorld.QueueCalls == 0 && resumeWorld.Owned == 3,
    "resume reconciles an interrupted confirmed registration without duplicate clicking");
Check(!File.Exists(resumePath), "successful resumed production deletes the session checkpoint");

var shrunkResumePlan = plan with { TargetQuantity = 6 };
var shrunkResumeWorld = new FakeWorld(shrunkResumePlan) { Owned = 3 };
string shrunkResumePath = Path.Combine(Path.GetTempPath(), "mabi-altering-resume-shrink-" + Guid.NewGuid().ToString("N") + ".json");
var shrunkResumeStore = new AlteringSessionStore(shrunkResumePath);
var shrunkResumeSession = AlteringSessionState.Create(shrunkResumePlan, testIdentity, 0, 0) with
{
    QueuedWorks = 1,
    PendingRegistration = true,
    PendingBeforeMatchingCount = 1,
    Stage = "작업 등록 확인"
};
shrunkResumeStore.Save(shrunkResumeSession);
var shrunkResumeAuto = new AlteringAutomation(
    shrunkResumeWorld, shrunkResumeWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, sessionStore: shrunkResumeStore, session: shrunkResumeSession);
await shrunkResumeAuto.RunAsync(shrunkResumePlan, default);
Check(shrunkResumeAuto.QueuedWorks == 2 && shrunkResumeWorld.QueueCalls == 1 && shrunkResumeWorld.Owned == 6,
    "resume accepts queue shrink only when received output proves the interrupted registration was not added");
Check(!File.Exists(shrunkResumePath), "queue-shrink resume completes and removes the checkpoint");

var ambiguousResumeWorld = new FakeWorld(shrunkResumePlan) { Owned = 6 };
string ambiguousResumePath = Path.Combine(Path.GetTempPath(), "mabi-altering-resume-ambiguous-" + Guid.NewGuid().ToString("N") + ".json");
var ambiguousResumeStore = new AlteringSessionStore(ambiguousResumePath);
var ambiguousResumeSession = AlteringSessionState.Create(shrunkResumePlan, testIdentity, 0, 0) with
{
    QueuedWorks = 1,
    PendingRegistration = true,
    PendingBeforeMatchingCount = 1,
    Stage = "작업 등록 확인"
};
ambiguousResumeStore.Save(ambiguousResumeSession);
var ambiguousResumeAuto = new AlteringAutomation(
    ambiguousResumeWorld, ambiguousResumeWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, sessionStore: ambiguousResumeStore, session: ambiguousResumeSession);
await ambiguousResumeAuto.RunAsync(shrunkResumePlan, default);
Check(ambiguousResumeAuto.QueuedWorks == 2 && ambiguousResumeWorld.QueueCalls == 0 &&
      ambiguousResumeWorld.Owned == 6,
    "resume credits authoritative output quantity when the old pending-registration count is ambiguous");
Check(!File.Exists(ambiguousResumePath), "quantity-reconciled resume removes the checkpoint");

var manualCleanupPlan = new AlteringPlan("식재료 가공 시설", "물에 불린 쌀", 100, 5, false);
var manualCleanupWorld = new FakeWorld(manualCleanupPlan) { Owned = 20 };
string manualCleanupPath = Path.Combine(Path.GetTempPath(), "mabi-altering-resume-manual-cleanup-" + Guid.NewGuid().ToString("N") + ".json");
var manualCleanupStore = new AlteringSessionStore(manualCleanupPath);
var manualCleanupSession = AlteringSessionState.Create(manualCleanupPlan, testIdentity, 0, 0) with
{
    QueuedWorks = 6,
    LastObservedOutputQuantity = 5,
    PendingRegistration = true,
    PendingBeforeMatchingCount = 5,
    Stage = "작업 등록 확인"
};
manualCleanupStore.Save(manualCleanupSession);
var manualCleanupAuto = new AlteringAutomation(
    manualCleanupWorld, manualCleanupWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, sessionStore: manualCleanupStore, session: manualCleanupSession);
await manualCleanupAuto.RunAsync(manualCleanupPlan, default);
Check(manualCleanupAuto.QueuedWorks == 20 && manualCleanupWorld.QueueCalls == 16 &&
      manualCleanupWorld.Owned == 100,
    "resume re-bases progress after completed jobs were received and remaining queued jobs were manually cancelled");
Check(!File.Exists(manualCleanupPath), "manual receive/cancel reconciliation completes and clears the checkpoint");

var extraQueuePlan = plan with { TargetQuantity = 3 };
var extraQueueWorld = new FakeWorld(extraQueuePlan);
extraQueueWorld.AddPending();
string extraQueuePath = Path.Combine(Path.GetTempPath(), "mabi-altering-extra-" + Guid.NewGuid().ToString("N") + ".json");
var extraQueueStore = new AlteringSessionStore(extraQueuePath);
var extraQueueSession = AlteringSessionState.Create(extraQueuePlan, testIdentity, 0, 0);
extraQueueStore.Save(extraQueueSession);
try
{
    await new AlteringAutomation(
        extraQueueWorld, extraQueueWorld,
        (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
        4, sessionStore: extraQueueStore, session: extraQueueSession)
        .RunAsync(extraQueuePlan, default);
    throw new Exception("unknown extra queue work accepted during resume");
}
catch (InvalidOperationException)
{
    Check(extraQueueWorld.QueueCalls == 0,
        "resume stops before input when live same-item queue exceeds saved maximum");
}
extraQueueStore.Delete();

var decreasedOutputWorld = new FakeWorld(extraQueuePlan);
string decreasedPath = Path.Combine(Path.GetTempPath(), "mabi-altering-decrease-" + Guid.NewGuid().ToString("N") + ".json");
var decreasedStore = new AlteringSessionStore(decreasedPath);
var decreasedSession = AlteringSessionState.Create(extraQueuePlan, testIdentity, 0, 0) with
{
    LastObservedOutputQuantity = 5
};
decreasedStore.Save(decreasedSession);
try
{
    await new AlteringAutomation(
        decreasedOutputWorld, decreasedOutputWorld,
        (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
        4, sessionStore: decreasedStore, session: decreasedSession)
        .RunAsync(extraQueuePlan, default);
    throw new Exception("decreased output inventory accepted during resume");
}
catch (InvalidOperationException)
{
    Check(decreasedOutputWorld.QueueCalls == 0,
        "resume stops before registration when observed output inventory decreased");
}
decreasedStore.Delete();

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

var recursiveWorld = new RecursiveProductionWorld();
var recursiveResolver = new RecursiveAlteringSupplyResolver(
    recursiveWorld, recursiveWorld, recursiveWorld, recursiveWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
var steelPlan = new AlteringPlan("금속 가공 시설", "강철괴", 3, 3, false);
var steelAuto = new AlteringAutomation(
    recursiveWorld, recursiveWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, recursiveResolver);
await steelAuto.RunAsync(steelPlan, default);
Check(recursiveWorld.Count("강철괴") == 3 &&
      recursiveWorld.GatherStarts == 1 &&
      recursiveWorld.Queued.SequenceEqual(new[] { "철괴(철 광석)", "강철괴" }),
    "steel recursively gathers iron ore, produces iron ingot, then resumes steel");
Check(steelAuto.ReservedWings == 0 && recursiveWorld.ReserveCallbackCalls == 0,
    "recursive steel production never reserves Spirit Wings");

// End-to-end F9 dependency scheduler: one intermediate lease fills seven
// leather slots, receives the batch, and yields back to seven leather+ jobs.
var integratedLeatherWorld = new RecursiveProductionWorld { IncludeLeather = true };
integratedLeatherWorld.SetCount("생가죽", 500);
integratedLeatherWorld.SetCount("타닌 가루", 28);
string integratedLeatherDir = Path.Combine(
    Path.GetTempPath(), "mabi-seven-leather-" + Guid.NewGuid().ToString("N"));
var integratedLeatherLane = new FacilityLaneState(
    await integratedLeatherWorld.WorksAsync(default));
var integratedLeatherScheduler = new MultiAlteringDependencyScheduler(
    integratedLeatherWorld, integratedLeatherWorld, testIdentity,
    integratedLeatherDir,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4, laneState: integratedLeatherLane);
var integratedLeatherResolver = new RecursiveAlteringSupplyResolver(
    integratedLeatherWorld, integratedLeatherWorld,
    integratedLeatherWorld, integratedLeatherWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4, dependencyScheduler: integratedLeatherScheduler);
var integratedLeatherAuto = new AlteringAutomation(
    integratedLeatherWorld, integratedLeatherWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, integratedLeatherResolver, facilityState: integratedLeatherLane);
var integratedLeatherResult = await integratedLeatherAuto.RunBatchAsync(
    leatherSevenPlan, 7, default);
Check(integratedLeatherResult == AlteringRunResult.BatchQueued &&
      integratedLeatherWorld.Queued.Take(7).All(x => x == "가죽") &&
      integratedLeatherWorld.Queued.Skip(7).Take(7).All(x => x == "가죽+") &&
      integratedLeatherWorld.Queued.Count == 14 &&
      integratedLeatherLane.Snapshot("가죽 가공 시설").IntermediateRegisteredWorks == 7 &&
      !integratedLeatherLane.Snapshot("가죽 가공 시설").IntermediateLease &&
      integratedLeatherWorld.GatherStarts == 0 &&
      integratedLeatherAuto.ReservedWings == 0,
    "real dependency coordinator fills seven leather slots before seven parent registrations and releases lease");
if (Directory.Exists(integratedLeatherDir))
    Directory.Delete(integratedLeatherDir, recursive: true);

// Partial stock on the real dependency coordinator must safely drain the
// three-slot child, release its lease, then use those three outputs. A fake
// resolver alone would miss a terminal-state / scheduler deadlock.
var integratedPartialWorld = new RecursiveProductionWorld { IncludeLeather = true };
integratedPartialWorld.SetCount("생가죽", 35);
integratedPartialWorld.SetCount("타닌 가루", 28);
string integratedPartialDir = Path.Combine(
    Path.GetTempPath(), "mabi-partial-leather-" + Guid.NewGuid().ToString("N"));
var integratedPartialLane = new FacilityLaneState(
    await integratedPartialWorld.WorksAsync(default));
var integratedPartialScheduler = new MultiAlteringDependencyScheduler(
    integratedPartialWorld, integratedPartialWorld, testIdentity,
    integratedPartialDir,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4, laneState: integratedPartialLane);
var integratedPartialResolver = new RecursiveAlteringSupplyResolver(
    integratedPartialWorld, integratedPartialWorld,
    integratedPartialWorld, integratedPartialWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4, dependencyScheduler: integratedPartialScheduler);
var integratedPartialAuto = new AlteringAutomation(
    integratedPartialWorld, integratedPartialWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, integratedPartialResolver, facilityState: integratedPartialLane);
var integratedPartialResult = await integratedPartialAuto.RunBatchAsync(
    leatherSevenPlan, 7, default);
Check(integratedPartialResult == AlteringRunResult.BatchQueued &&
      integratedPartialWorld.Queued.Count(x => x == "가죽") == 3 &&
      integratedPartialWorld.Queued.Count(x => x == "가죽+") == 3 &&
      integratedPartialWorld.Count("생가죽") == 5 &&
      integratedPartialLane.Snapshot("가죽 가공 시설").IntermediateRegisteredWorks == 3 &&
      !integratedPartialLane.Snapshot("가죽 가공 시설").IntermediateLease &&
      integratedPartialWorld.GatherStarts == 0 &&
      integratedPartialAuto.ReservedWings == 0,
    "real dependency coordinator uses 3 confirmed child receipts under 35 hides and returns partial stock to parent");
if (Directory.Exists(integratedPartialDir))
    Directory.Delete(integratedPartialDir, recursive: true);

var scheduledWorld = new RecursiveProductionWorld();
scheduledWorld.AddExternalWork(
    "강철괴",
    "금속 가공 시설",
    "InProgress",
    isCompleted: false,
    remainingSeconds: 8);
var scheduledLaneState = new FacilityLaneState(
    await scheduledWorld.WorksAsync(default));
int dependencyBoundaryWaits = 0;
string dependencySessionDir = Path.Combine(
    Path.GetTempPath(),
    "mabi-dependency-scheduler-" + Guid.NewGuid().ToString("N"));
var dependencyScheduler = new MultiAlteringDependencyScheduler(
    scheduledWorld,
    scheduledWorld,
    testIdentity,
    dependencySessionDir,
    (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        dependencyBoundaryWaits++;
        scheduledWorld.CompleteAllWorks();
        return Task.CompletedTask;
    },
    verificationAttempts: 4,
    laneState: scheduledLaneState);
bool sawIntegratedDependencyLog = false;
dependencyScheduler.Log += text =>
    sawIntegratedDependencyLog |= text.Contains(
        "메인과 동일한 시설 7칸 Coordinator 시작",
        StringComparison.Ordinal);
var scheduledResolver = new RecursiveAlteringSupplyResolver(
    scheduledWorld,
    scheduledWorld,
    scheduledWorld,
    scheduledWorld,
    delay: (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    },
    verificationAttempts: 4,
    dependencyScheduler: dependencyScheduler);
var scheduledSteelAuto = new AlteringAutomation(
    scheduledWorld,
    scheduledWorld,
    (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    },
    4,
    scheduledResolver);
await scheduledSteelAuto.RunAsync(steelPlan, default);
Check(dependencyBoundaryWaits > 0 &&
      sawIntegratedDependencyLog &&
      scheduledWorld.Queued.SequenceEqual(new[] { "철괴(철 광석)", "강철괴" }),
    "multi dependency scheduler waits for the whole existing facility batch, then schedules intermediate processing through the shared seven-slot coordinator");
Check(scheduledWorld.Count("강철괴") == 6 &&
      scheduledWorld.Count("철괴") == 0,
    "existing completed root work is received before dependency ownership and intermediate stock is consumed only by the resumed parent");
var scheduledOwnership = scheduledLaneState.Snapshot("금속 가공 시설");
Check(scheduledOwnership.InitialObservedWorks == 1 &&
      scheduledOwnership.IntermediateRegisteredWorks == 1 &&
      !scheduledOwnership.IntermediateLease,
    "dependency scheduler owns the empty facility lane only for its intermediate batch and releases it after receipt");
var recoveredSessionWorld = new RecursiveProductionWorld();
recoveredSessionWorld.AddExternalWork(
    "철괴", "금속 가공 시설", "Completed", isCompleted: true, remainingSeconds: 0);
string satisfiedSessionDir = Path.Combine(
    Path.GetTempPath(), "mabi-satisfied-dependency-" + Guid.NewGuid().ToString("N"));
var satisfiedIronPlan = new AlteringPlan("금속 가공 시설", "철괴(철 광석)", 3, 3, false);
var staleDependencyStore = new AlteringSessionStore(
    AlteringSessionStore.MultiPlanPath(
        Path.Combine(satisfiedSessionDir, "dependencies"), satisfiedIronPlan));
staleDependencyStore.Save(
    AlteringSessionState.Create(satisfiedIronPlan, testIdentity, 0, 0) with
    {
        QueuedWorks = 1,
        Stage = "중단된 중간재료 배치"
    });
var recoveredLane = new FacilityLaneState(
    await recoveredSessionWorld.WorksAsync(default));
var recoveredScheduler = new MultiAlteringDependencyScheduler(
    recoveredSessionWorld,
    recoveredSessionWorld,
    testIdentity,
    satisfiedSessionDir,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    laneState: recoveredLane);
var recoveredResolver = new RecursiveAlteringSupplyResolver(
    recoveredSessionWorld,
    recoveredSessionWorld,
    recoveredSessionWorld,
    recoveredSessionWorld,
    verificationAttempts: 4);
await recoveredScheduler.RunAsync(
    satisfiedIronPlan,
    0,
    3,
    recoveredResolver,
    default);
Check(!File.Exists(staleDependencyStore.Path) &&
      recoveredWorldQueueIsUnchanged() &&
      recoveredSessionWorld.Count("철괴") == 3 &&
      recoveredLane.Snapshot("금속 가공 시설").IntermediateDepth == 0,
    "satisfied dependency deletes stale resume state after an existing completed batch is received");
Check(recoveredLane.QueueDirectiveFor(satisfiedIronPlan.FacilityName) ==
          AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite &&
      recoveredLane.Snapshot(satisfiedIronPlan.FacilityName).LiveWorks == 0 &&
      recoveredLane.QueueDirectiveFor("목재 가공 시설") ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "H4: dependency boundary receipt confirms only the proven same-facility onsite state after all seven-slot works are gone");


bool recoveredWorldQueueIsUnchanged() => recoveredSessionWorld.Queued.Count == 0;

// Repeating the same intermediate request must start from a clean checkpoint,
// not inherit the obsolete queued-work count from the prior satisfied request.
recoveredSessionWorld.SetCount("철 광석", 10);
await recoveredScheduler.RunAsync(
    satisfiedIronPlan,
    3,
    3,
    recoveredResolver,
    default);
Check(recoveredSessionWorld.Count("철괴") == 6 &&
      recoveredSessionWorld.Queued.SequenceEqual(new[] { "철괴(철 광석)" }) &&
      !File.Exists(staleDependencyStore.Path),
    "later request for the same intermediate starts fresh after completed-work checkpoint cleanup");
Check(recoveredSessionWorld.QueueDirectives.SequenceEqual(
          new[] { AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite }),
    "H4: first intermediate registration after boundary receipt reuses manager-confirmed facility without second move");

var residualWorld = new RecursiveProductionWorld
{
    InjectResidualAfterReceipt = true
};
residualWorld.AddExternalWork(
    "철괴", "금속 가공 시설", "Completed", isCompleted: true, remainingSeconds: 0);
residualWorld.AddExternalWork(
    "철괴", "금속 가공 시설", "Completed", isCompleted: true, remainingSeconds: 0);
var residualLane = new FacilityLaneState(await residualWorld.WorksAsync(default));
var residualScheduler = new MultiAlteringDependencyScheduler(
    residualWorld,
    residualWorld,
    testIdentity,
    Path.Combine(Path.GetTempPath(),
        "mabi-h4-residual-" + Guid.NewGuid().ToString("N")),
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    laneState: residualLane);
var residualResolver = new RecursiveAlteringSupplyResolver(
    residualWorld, residualWorld, residualWorld, residualWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
bool residualRejected = false;
try
{
    await residualScheduler.RunAsync(
        satisfiedIronPlan, 0, 9, residualResolver, default);
}
catch (InvalidOperationException ex)
{
    // M4 now rejects a partial receive earlier, before the dependency
    // scheduler's post-collection lane observation. Both fail closed, but the
    // earlier receipt guard must not rewrite the manager ledger as success.
    residualRejected =
        ex.Message.Contains("시설 전체 대기열 0건") ||
        ex.Message.Contains("작업이 남아 있어 중간재료가 시설을 소유하지 않습니다");
}
Check(residualRejected &&
      residualLane.QueueDirectiveFor("금속 가공 시설") ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      (await residualWorld.WorksAsync(default)).Count(x =>
          x.FacilityName == "금속 가공 시설") == 1 &&
      residualLane.Snapshot("금속 가공 시설").LiveWorks >= 1 &&
      residualWorld.QueueDirectives.Count == 0,
    "H4: residual facility work after boundary receipt prevents onsite confirmation and intermediate registration");


if (Directory.Exists(satisfiedSessionDir))
    Directory.Delete(satisfiedSessionDir, recursive: true);


var frozenBoundaryWorld = new RecursiveProductionWorld();
frozenBoundaryWorld.AddExternalWork(
    "철괴", "금속 가공 시설", "InProgress", isCompleted: false, remainingSeconds: 90);
var frozenBoundaryLane = new FacilityLaneState(
    await frozenBoundaryWorld.WorksAsync(default));
frozenBoundaryLane.ConfirmOnsite("금속 가공 시설", "F06 frozen boundary initial proof");
DateTimeOffset frozenBoundaryClock = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
var frozenBoundaryScheduler = new MultiAlteringDependencyScheduler(
    frozenBoundaryWorld, frozenBoundaryWorld, testIdentity,
    Path.Combine(Path.GetTempPath(), "mabi-f06-frozen-" + Guid.NewGuid().ToString("N")),
    (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        frozenBoundaryClock = frozenBoundaryClock.AddSeconds(6);
        return Task.CompletedTask;
    },
    verificationAttempts: 4,
    laneState: frozenBoundaryLane,
    boundaryNow: () => frozenBoundaryClock,
    boundaryIdleThreshold: TimeSpan.FromSeconds(5));
var frozenBoundaryResolver = new RecursiveAlteringSupplyResolver(
    frozenBoundaryWorld, frozenBoundaryWorld, frozenBoundaryWorld, frozenBoundaryWorld);
bool frozenBoundaryStopped = false;
try
{
    await frozenBoundaryScheduler.RunAsync(
        new AlteringPlan("금속 가공 시설", "철괴(철 광석)", 3, 3, false),
        0, 3, frozenBoundaryResolver, default);
}
catch (InvalidOperationException ex)
{
    frozenBoundaryStopped = ex.Message.Contains("다중가공 시설별 정체 감지",
        StringComparison.Ordinal);
}
Check(frozenBoundaryStopped &&
      frozenBoundaryWorld.Queued.Count == 0 &&
      frozenBoundaryLane.QueueDirectiveFor("금속 가공 시설") ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "F06 stalled intermediate boundary stops without input and invalidates onsite proof");

var nestedProductionWorld = new RecursiveProductionWorld
{
    NestedSameFacilityChain = true
};
nestedProductionWorld.SetCount("철괴", 3); // Enough for one 합금괴, not both.
nestedProductionWorld.SetCount("철 광석", 0);
string nestedSessionDir = Path.Combine(
    Path.GetTempPath(), "mabi-nested-dependency-" + Guid.NewGuid().ToString("N"));
var nestedProductionLane = new FacilityLaneState(Array.Empty<AlteringWork>());
var nestedConsumptionObserver = new CountingInternalConsumptionObserver();
var nestedProductionScheduler = new MultiAlteringDependencyScheduler(
    nestedProductionWorld, nestedProductionWorld, testIdentity, nestedSessionDir,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4, laneState: nestedProductionLane,
    internalConsumptionObserver: nestedConsumptionObserver);
var nestedProductionResolver = new RecursiveAlteringSupplyResolver(
    nestedProductionWorld,
    nestedProductionWorld,
    nestedProductionWorld,
    nestedProductionWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    dependencyScheduler: nestedProductionScheduler);
var nestedFinalPlan = new AlteringPlan("금속 가공 시설", "합성괴", 1, 1, false);
var nestedFinalAutomation = new AlteringAutomation(
    nestedProductionWorld,
    nestedProductionWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4,
    nestedProductionResolver);
await nestedFinalAutomation.RunAsync(nestedFinalPlan, default);
Check(nestedProductionWorld.Queued.SequenceEqual(new[]
    { "합금괴", "철괴(철 광석)", "합금괴", "합성괴" }) &&
      nestedProductionWorld.Count("합성괴") == 1 &&
      nestedProductionLane.Snapshot("금속 가공 시설").IntermediateRegisteredWorks == 3 &&
      nestedProductionLane.Snapshot("금속 가공 시설").IntermediateDepth == 0,
    "three-stage same-facility recursive dependency restores parent lease after child batch");
Check(nestedConsumptionObserver.Captures >= 3 &&
      nestedConsumptionObserver.Captures == nestedConsumptionObserver.Commits,
    "F05 recursive intermediate registrations share the main-run consumption observer");
if (Directory.Exists(nestedSessionDir))
    Directory.Delete(nestedSessionDir, recursive: true);

if (Directory.Exists(dependencySessionDir))
    Directory.Delete(dependencySessionDir, recursive: true);

// 2026-10-17 actual F9: 밥 100개 was selected with "물이 든 병 0/2",
// but a global queue_full and earlier wood failure prevented food preparation.
// F9 now plans a safe raw-gather pass independently of facility service order.
var riceWorld = new RecursiveProductionWorld { IncludeRice = true };
riceWorld.SetCount("물이 든 병", 0);
riceWorld.AddExternalWork("이전 식재료", "식재료 가공 시설", "Completed", true, 0);
var ricePlan = new AlteringPlan("식재료 가공 시설", "밥", 100, 3, false);
var riceSelected = new AlteringRecipe(
    "밥", false, 3, "not_enough_ingredient",
    new[] { new AlteringIngredient("물이 든 병", 2, 0) }, ricePlan.FacilityName);
var riceQueueFull = riceSelected with
{
    Reason = "queue_full",
    MissingIngredients = Array.Empty<AlteringIngredient>()
};
var riceQueueEvidence = new[]
{
    new MultiAlteringSupplyPreflight(ricePlan, riceSelected, ricePlan.RequiredWorks)
};
var riceRequests = MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
    new[] { ricePlan }, new[] { riceQueueFull }, riceQueueEvidence);
Check(riceRequests.Count == 1 &&
      riceRequests[0].Recipe.MissingIngredients.Single().DisplayName == "물이 든 병" &&
      riceRequests[0].RemainingWorks == 34,
    "F9 retains verified rice-water shortage when global queue_full masks recipe evidence");
// Craftable leather shortage is handled per ticket by the recursive resolver;
// it must not trigger the GLOBAL free-gather pass before facility activation.
var leatherPreflightPlan = new AlteringPlan("가죽 가공 시설", "가죽+", 9, 3, false);
var leatherPreflightRecipe = new AlteringRecipe(
    "가죽+", false, 3, "not_enough_ingredient",
    new[] { new AlteringIngredient("가죽", 1, 0) }, "가죽 가공 시설");
var leatherPreflightEvidence = new[]
{
    new MultiAlteringSupplyPreflight(
        leatherPreflightPlan, leatherPreflightRecipe, leatherPreflightPlan.RequiredWorks)
};
var mixedPreflight = MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
    new[] { leatherPreflightPlan, ricePlan },
    new[] { leatherPreflightRecipe, riceSelected },
    Array.Empty<MultiAlteringSupplyPreflight>());
Check(mixedPreflight.Count == 1 && mixedPreflight[0].Plan == ricePlan,
    "F9 skips leather intermediates in global gather but preserves other facilities");
Check(MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
        new[] { leatherPreflightPlan },
        new[] { leatherPreflightRecipe with {
            Reason = "queue_full", MissingIngredients = Array.Empty<AlteringIngredient>() } },
        leatherPreflightEvidence).Count == 0,
    "F9 never resurrects masked leather+ shortage from queued UI evidence");
var riceResolver = new RecursiveAlteringSupplyResolver(
    riceWorld, riceWorld, riceWorld, riceWorld,
    delay: (_, ct) => { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
// Defensive entry-point check: global pre-gather cannot prematurely process leather.
int preLeatherGatherCount = riceWorld.Gathered.Count;
int preLeatherFieldExits = riceWorld.FieldExitCalls;
await riceResolver.PreGatherKnownShortagesAsync(leatherPreflightEvidence, default);
Check(riceWorld.Gathered.Count == preLeatherGatherCount &&
      riceWorld.FieldExitCalls == preLeatherFieldExits,
    "global pre-gather bypasses leather without touching the game screen");
await riceResolver.PreGatherKnownShortagesAsync(riceRequests, default);
Check(riceWorld.Count("물이 든 병") >= 68 &&
      riceWorld.Gathered.SequenceEqual(new[] { "물이 든 병" }) &&
      riceWorld.FieldExitCalls == 1 &&
      (await riceWorld.WorksAsync(default)).Count == 1,
    "F9 acquires 34x2 rice water from the well route before wood/food lanes, without collecting old jobs");
await riceWorld.QueueAsync(ricePlan, () => { }, default);
Check(riceWorld.Queued.SequenceEqual(new[] { "밥" }) &&
      riceWorld.Count("물이 든 병") >= 66,
    "rice can register after gathered water satisfies a fresh live ingredient check");
Check(MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
        new[] { ricePlan }, new[] { riceQueueFull },
        Array.Empty<MultiAlteringSupplyPreflight>()).Count == 0 &&
      MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
        new[] { ricePlan }, new[] { riceQueueFull },
        new[] { riceQueueEvidence[0] with {
            Plan = ricePlan with { TargetQuantity = 99 } } }).Count == 0 &&
      MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
        new[] { ricePlan }, new[] {
            riceQueueFull with { Reason = "unknown_recipe_block" } },
        riceQueueEvidence).Count == 0,
    "F9 never invents water shortages from missing, mismatched or unknown-condition evidence");
Check(MultiAlteringMaterialPreflightPolicy.SelectInitialKnownShortages(
        new[] { ricePlan }, new[] { riceSelected },
        Array.Empty<MultiAlteringSupplyPreflight>()).Count == 1,
    "fresh authoritative CLI water shortage triggers early gathering without UI evidence");

var multiWorld = new RecursiveProductionWorld();
multiWorld.SetCount("석탄", 0);
var multiLane = new FacilityLaneState(Array.Empty<AlteringWork>());
multiLane.ConfirmOnsite(steelPlan.FacilityName,
    "H5 test: previous intermediate confirmed metal facility");
multiWorld.ManagerObservedDuringGather = multiLane;
var multiResolver = new RecursiveAlteringSupplyResolver(
    multiWorld, multiWorld, multiWorld, multiWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    laneState: multiLane);
var multiAuto = new AlteringAutomation(
    multiWorld, multiWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4, multiResolver);
await multiAuto.RunAsync(steelPlan, default);
Check(multiWorld.Count("강철괴") == 3 &&
      multiWorld.GatherStarts == 2 &&
      multiWorld.Gathered.SequenceEqual(new[] { "철 광석", "석탄" }) &&
      multiWorld.FieldExitCalls == 1,
    "multi-gather batches iron ore and coal in one field transition before recursive processing");
Check(multiWorld.Queued.SequenceEqual(new[] { "철괴(철 광석)", "강철괴" }),
    "multi-gather preserves recursive caller return: intermediate iron then final steel");
Check(multiLane.QueueDirectiveFor(steelPlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      multiWorld.LaneDirectivesAtGatherStart.Count == 2 &&
      multiWorld.LaneDirectivesAtGatherStart.All(d =>
          d == AlteringFacilityEntryDirective.FreshMoveRequired),
    "H5: after an intermediate onsite confirmation, a subsequent grouped field exit clears manager onsite before gathering input");

var wholePlanWorld = new RecursiveProductionWorld();
wholePlanWorld.SetCount("석탄", 0);
wholePlanWorld.SetCount("철 광석", 0);
var preflightLane = new FacilityLaneState(Array.Empty<AlteringWork>());
preflightLane.ConfirmOnsite(steelPlan.FacilityName, "H5 test: previously confirmed facility");
wholePlanWorld.ManagerObservedDuringGather = preflightLane;
var wholePlanResolver = new RecursiveAlteringSupplyResolver(
    wholePlanWorld, wholePlanWorld, wholePlanWorld, wholePlanWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    laneState: preflightLane);
var wholeSteelRecipe = (await wholePlanWorld.RecipesAsync(default))
    .Single(x => x.DisplayName == "강철괴");
var alloyPlan = new AlteringPlan("금속 가공 시설", "합금괴", 3, 3, false);
var alloyRecipe = new AlteringRecipe(
    "합금괴",
    false,
    3,
    "not_enough_ingredient",
    new[]
    {
        new AlteringIngredient("철괴", 3, 0),
        new AlteringIngredient("석탄", 2, 0)
    },
    "금속 가공 시설");
await wholePlanResolver.PreGatherKnownShortagesAsync(
    new[]
    {
        new MultiAlteringSupplyPreflight(steelPlan, wholeSteelRecipe, 1),
        new MultiAlteringSupplyPreflight(alloyPlan, alloyRecipe, 1)
    },
    default);
Check(wholePlanWorld.FieldExitCalls == 1 &&
      wholePlanWorld.GatherStarts == 2 &&
      wholePlanWorld.Gathered.Count(x => x == "철 광석") == 1 &&
      wholePlanWorld.Gathered.Count(x => x == "석탄") == 1 &&
      wholePlanWorld.Count("철 광석") >= 20 &&
      wholePlanWorld.Count("석탄") >= 6,
    "whole-plan material preflight merges shared proven raw shortages into one field gathering session");
Check(preflightLane.QueueDirectiveFor(steelPlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      wholePlanWorld.LaneDirectivesAtGatherStart.Count == 2 &&
      wholePlanWorld.LaneDirectivesAtGatherStart.All(d =>
          d == AlteringFacilityEntryDirective.FreshMoveRequired),
    "H5: whole-plan preflight announces confirmed field exit to the shared manager before raw gathering");

var fallbackWorld = new RecursiveProductionWorld
{
    HideGatheringCatalogOnFirstQuery = true
};
fallbackWorld.SetCount("석탄", 0);
var fallbackLane = new FacilityLaneState(Array.Empty<AlteringWork>());
fallbackLane.ConfirmOnsite(steelPlan.FacilityName,
    "H5 test: recursive intermediate already confirmed onsite");
fallbackWorld.ManagerObservedDuringGather = fallbackLane;
var fallbackResolver = new RecursiveAlteringSupplyResolver(
    fallbackWorld, fallbackWorld, fallbackWorld, fallbackWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    laneState: fallbackLane);
var fallbackBlocked = new AlteringRecipe(
    steelPlan.DisplayName, false, steelPlan.ProducedPerWork,
    "not_enough_ingredient",
    new[] { new AlteringIngredient("석탄", 4, 0) },
    steelPlan.FacilityName);
await fallbackResolver.ResolveAsync(steelPlan, fallbackBlocked, 1, default);
Check(fallbackWorld.FieldExitCalls == 1 &&
      fallbackWorld.GatherStarts == 1 &&
      fallbackWorld.LaneDirectivesAtGatherStart.SequenceEqual(
          new[] { AlteringFacilityEntryDirective.FreshMoveRequired }) &&
      fallbackLane.QueueDirectiveFor(steelPlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "H5: hidden raw shortage discovered after intermediate processing clears coordinator onsite in recursive single-material fallback");

var failedExitWorld = new RecursiveProductionWorld
{
    HideGatheringCatalogOnFirstQuery = true,
    FailFieldExit = true
};
failedExitWorld.SetCount("석탄", 0);
var failedExitLane = new FacilityLaneState(Array.Empty<AlteringWork>());
failedExitLane.ConfirmOnsite(steelPlan.FacilityName,
    "H5 test: uncertain navigation from confirmed intermediate facility");
var failedExitResolver = new RecursiveAlteringSupplyResolver(
    failedExitWorld, failedExitWorld, failedExitWorld, failedExitWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4,
    laneState: failedExitLane);
bool exitFailed = false;
try
{
    await failedExitResolver.ResolveAsync(steelPlan, fallbackBlocked, 1, default);
}
catch (InvalidOperationException ex)
{
    exitFailed = ex.Message.Contains("H5 simulated navigation failure");
}
Check(exitFailed &&
      failedExitWorld.FieldExitCalls == 1 &&
      failedExitWorld.GatherStarts == 0 &&
      failedExitLane.QueueDirectiveFor(steelPlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "H5: uncertain or failed field exit invalidates stale onsite and prevents subsequent gathering input");

var noDepartureWorld = new RecursiveProductionWorld();
noDepartureWorld.SetCount("석탄", 12);
var noDepartureLane = new FacilityLaneState(Array.Empty<AlteringWork>());
noDepartureLane.ConfirmOnsite(steelPlan.FacilityName,
    "H5 test: no field transition required");
var noDepartureResolver = new RecursiveAlteringSupplyResolver(
    noDepartureWorld, noDepartureWorld,
    noDepartureWorld, noDepartureWorld,
    verificationAttempts: 4, laneState: noDepartureLane);
await noDepartureResolver.PreGatherKnownShortagesAsync(
    new[] { new MultiAlteringSupplyPreflight(steelPlan, fallbackBlocked, 1) },
    default);
Check(noDepartureWorld.FieldExitCalls == 0 &&
      noDepartureLane.QueueDirectiveFor(steelPlan.FacilityName) ==
          AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
    "H5: resolver preflight without actual field departure preserves the manager's existing onsite proof");


var skipWorld = new RecursiveProductionWorld();
skipWorld.SetCount("철 광석", 20);
var skipCoordinator = new MultiGatheringCoordinator(
    skipWorld, skipWorld,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    4);
await skipCoordinator.RunAsync(
    new[] { new MultiGatheringRequest("철 광석", 10, steelPlan) },
    default);
Check(skipWorld.GatherStarts == 0,
    "multi-gather rechecks inventory immediately before each material and skips an already-satisfied target");

// F9 V3.1.80: an untouched or already-empty facility has no work to stall.
// It must not time out during a 30+ minute material-gathering phase elsewhere.
// When real work later appears, it receives a fresh, independent watchdog.
DateTimeOffset f9EmptyClock = new(2026, 10, 11, 0, 0, 0, TimeSpan.Zero);
var f9EmptyWatch = new MultiAlteringWaitWatchdog(
    TimeSpan.FromSeconds(120), () => f9EmptyClock);
const string f9EmptyFacility = "금속 가공 시설";
f9EmptyWatch.Observe(f9EmptyFacility, Array.Empty<AlteringWork>());
f9EmptyClock = f9EmptyClock.AddMinutes(31);
f9EmptyWatch.Observe(f9EmptyFacility, Array.Empty<AlteringWork>());
var f9LaterJob = new AlteringWork(
    "철괴", f9EmptyFacility, "InProgress", false, 300);
f9EmptyWatch.Observe(f9EmptyFacility, new[] { f9LaterJob });
f9EmptyClock = f9EmptyClock.AddSeconds(119);
f9EmptyWatch.Observe(f9EmptyFacility, new[] { f9LaterJob });
Check(true,
    "F9 empty facility stays idle for over 30 minutes without false 120-second stall");

f9EmptyClock = f9EmptyClock.AddSeconds(1);
f9EmptyWatch.Observe(f9EmptyFacility, Array.Empty<AlteringWork>());
f9EmptyClock = f9EmptyClock.AddMinutes(31);
f9EmptyWatch.Observe(f9EmptyFacility, Array.Empty<AlteringWork>());
f9EmptyWatch.Observe(f9EmptyFacility, new[] { f9LaterJob });
Check(true,
    "F9 empty facility resets stale countdown history before later registration");

// F9 V3.1.80: the manager may never treat an unexplained shrink from a
// one-slot registration run as a guarded receipt.
const string f9ShrinkFacility = "목재 가공 시설";
var f9ShrinkPlan = new AlteringPlan(f9ShrinkFacility, "목재", 3, 1, false);
var f9ShrinkWorks = new List<AlteringWork>
{
    new("목재", f9ShrinkFacility, "InProgress", false, 90)
};
var f9ShrinkLane = new FacilityLaneState(f9ShrinkWorks);
var f9ShrinkCoordinator = new MultiAlteringCoordinator(
    f9ShrinkLane, fillInitialVacancies: true);
int f9ShrinkAttempts = 0;
bool f9UnverifiedShrinkRejected = false;
try
{
    await f9ShrinkCoordinator.RunAsync(
        new[] { f9ShrinkPlan },
        (plan, slotBudget, token) =>
        {
            token.ThrowIfCancellationRequested();
            f9ShrinkAttempts++;
            Check(slotBudget == 1, "F9 shrink: coordinator grants only one registration");
            f9ShrinkWorks.Clear(); // external cancellation or a stale CLI snapshot
            return Task.FromResult(false); // no confirmed receipt callback
        },
        token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AlteringWork>>(f9ShrinkWorks.ToArray());
        },
        (duration, token) => Task.CompletedTask,
        default);
}
catch (InvalidOperationException ex)
{
    f9UnverifiedShrinkRejected =
        ex.Message.Contains("대기열이 자동화 수령 구간이 아닌데 감소했습니다");
}
Check(f9UnverifiedShrinkRejected && f9ShrinkAttempts == 1,
    "F9 unexpected queue shrink after runBatch fails closed without another registration");

var f9LeaseWorks = new AlteringWork[]
{
    new("목재", f9ShrinkFacility, "InProgress", false, 90)
};
var f9AcquireGuard = new FacilityLaneState(f9LeaseWorks);
bool f9AcquireShrinkRejected = false;
try
{
    f9AcquireGuard.AcquireIntermediate(
        f9ShrinkFacility, Array.Empty<AlteringWork>());
}
catch (InvalidOperationException ex)
{
    f9AcquireShrinkRejected =
        ex.Message.Contains("대기열이 자동화 수령 구간이 아닌데 감소했습니다");
}
Check(f9AcquireShrinkRejected &&
      !f9AcquireGuard.Snapshot(f9ShrinkFacility).IntermediateLease,
    "F9 intermediate lease cannot silently accept an unverified queue shrink");

var f9ReleaseGuard = new FacilityLaneState(Array.Empty<AlteringWork>());
f9ReleaseGuard.AcquireIntermediate(f9ShrinkFacility, Array.Empty<AlteringWork>());
f9ReleaseGuard.NoteRegistration(
    f9ShrinkPlan, FacilityLaneOwner.Intermediate, 1);
f9ReleaseGuard.Observe(f9ShrinkFacility, f9LeaseWorks, allowShrink: false);
bool f9ReleaseShrinkRejected = false;
try
{
    f9ReleaseGuard.ReleaseIntermediate(
        f9ShrinkFacility, Array.Empty<AlteringWork>());
}
catch (InvalidOperationException ex)
{
    f9ReleaseShrinkRejected =
        ex.Message.Contains("대기열이 자동화 수령 구간이 아닌데 감소했습니다");
}
Check(f9ReleaseShrinkRejected &&
      f9ReleaseGuard.Snapshot(f9ShrinkFacility).IntermediateLease,
    "F9 intermediate lease cannot release on an unverified queue shrink");

// M2 follow-up: the coordinator (not per-slot RunCoreAsync) observes idle
// queues across batch-yield cycles, with a separate timer per facility.
DateTimeOffset m2WatchClock = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
var m2Watch = new MultiAlteringWaitWatchdog(
    TimeSpan.FromSeconds(120), () => m2WatchClock);
var m2WatchWork = new AlteringWork(
    "목재", "목재 가공 시설", "InProgress", false, 90);
m2Watch.Observe("목재 가공 시설", new[] { m2WatchWork });
m2WatchClock = m2WatchClock.AddSeconds(119);
m2Watch.Observe("목재 가공 시설", new[] { m2WatchWork });
m2WatchClock = m2WatchClock.AddSeconds(2);
bool m2StagnantStopped = false;
try
{
    m2Watch.Observe("목재 가공 시설", new[] { m2WatchWork });
}
catch (InvalidOperationException ex)
{
    m2StagnantStopped = ex.Message.Contains("다중가공 시설별 정체 감지") &&
        ex.Message.Contains("임의 설비 이동/모두 받기/Space 재시도 없이 안전 정지");
}
Check(m2StagnantStopped,
    "M2 wait: unchanged CLI queue across repeated multi-batch polling stops safely after a bounded idle interval");

DateTimeOffset m2ProgressClock = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
var m2ProgressWatch = new MultiAlteringWaitWatchdog(
    TimeSpan.FromSeconds(120), () => m2ProgressClock);
m2ProgressWatch.Observe("금속 가공 시설",
    new[] { m2WatchWork with { FacilityName = "금속 가공 시설" } });
m2ProgressClock = m2ProgressClock.AddSeconds(110);
m2ProgressWatch.Observe("금속 가공 시설",
    new[] { m2WatchWork with { FacilityName = "금속 가공 시설", RemainingSeconds = 80 } });
m2ProgressClock = m2ProgressClock.AddSeconds(110);
m2ProgressWatch.Observe("금속 가공 시설",
    new[] { m2WatchWork with { FacilityName = "금속 가공 시설", RemainingSeconds = 60 } });
Check(true,
    "M2 wait: genuine countdown decreases refresh the facility watchdog during long legitimate processing");

DateTimeOffset m2NoiseClock = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
var m2NoiseWatch = new MultiAlteringWaitWatchdog(
    TimeSpan.FromSeconds(120), () => m2NoiseClock);
m2NoiseWatch.Observe("목재 가공 시설", new[] { m2WatchWork });
m2NoiseClock = m2NoiseClock.AddSeconds(100);
m2NoiseWatch.Observe("목재 가공 시설",
    new[] { m2WatchWork with { RemainingSeconds = 120 } });
m2NoiseClock = m2NoiseClock.AddSeconds(21);
bool m2NoiseRejected = false;
try
{
    m2NoiseWatch.Observe("목재 가공 시설",
        new[] { m2WatchWork with { RemainingSeconds = 120 } });
}
catch (InvalidOperationException)
{
    m2NoiseRejected = true;
}
Check(m2NoiseRejected,
    "M2 wait: an increasing or noisy countdown cannot conceal a truly stalled facility");

// Regression from live V3.1.77 2026-10-10 22:48:03:
// A guarded 1 -> 0 receipt and confirmed 0 -> 1 registration can replace
// a queue without changing its final count or InProgress/300s CLI shape.
// That registration MUST refresh this facility's old 120-second stopwatch.
async Task<(bool Stopped, int Registrations)> M2SameCountReplacementAsync(bool confirmedRegistration)
{
    const string facility = "가죽 가공 시설";
    var job = new AlteringPlan(facility, "가죽+", 3, 3, false);
    DateTimeOffset now = new(2026, 10, 10, 22, 45, 0, TimeSpan.FromHours(9));
    var works = new List<AlteringWork>
    {
        new(job.OutputName, facility, "InProgress", false, 300)
    };
    var lane = new FacilityLaneState(works);
    var coordinator = new MultiAlteringCoordinator(
        lane,
        now: () => now,
        idleThreshold: TimeSpan.FromSeconds(120),
        fillInitialVacancies: true);
    int runCalls = 0;
    try
    {
        await coordinator.RunAsync(
            new[] { job },
            (plan, budget, token) =>
            {
                token.ThrowIfCancellationRequested();
                runCalls++;
                if (runCalls == 1)
                {
                    Check(budget == 1, "M2 same-count refresh keeps one-slot engine");
                    now = now.AddSeconds(126);
                    if (confirmedRegistration)
                    {
                        // The protected batch has received the previous work
                        // and CLI-confirmed one newly registered work. Only the
                        // ledger evidence, not a same-size queue, proves this.
                        works.Clear();
                        lane.Observe(facility, works, allowShrink: true);
                        lane.NoteRegistration(plan, FacilityLaneOwner.Main, 1);
                        works.Add(new(job.OutputName, facility, "InProgress", false, 300));
                    }
                    return Task.FromResult(false);
                }
                // First pass must eventually yield to the manager's watchdog.
                // After that delay, a completed batch can finish normally.
                return Task.FromResult(works.All(x => x.IsCompleted));
            },
            token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<AlteringWork>>(works.ToArray());
            },
            (duration, token) =>
            {
                token.ThrowIfCancellationRequested();
                now += duration;
                for (int i = 0; i < works.Count; i++)
                    works[i] = works[i] with
                    {
                        State = "Completed",
                        IsCompleted = true,
                        RemainingSeconds = 0
                    };
                return Task.CompletedTask;
            },
            default);
        return (false, lane.Snapshot(facility).MainRegisteredWorks);
    }
    catch (InvalidOperationException ex) when (
        ex.Message.Contains("다중가공 시설별 정체 감지"))
    {
        return (true, lane.Snapshot(facility).MainRegisteredWorks);
    }
}
var m2ConfirmedReplay = await M2SameCountReplacementAsync(true);
Check(!m2ConfirmedReplay.Stopped && m2ConfirmedReplay.Registrations == 1,
    "M2 live 22:48 same-count 1->0->1 confirmed registration resets stale 126s watchdog");
var m2UnprovenReplay = await M2SameCountReplacementAsync(false);
Check(m2UnprovenReplay.Stopped && m2UnprovenReplay.Registrations == 0,
    "M2 same-count without CLI-confirmed registration still stops after 120s");

DateTimeOffset m2ScheduleClock = new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
var m2BlockedWorks = new List<AlteringWork>
{
    new("목재", "목재 가공 시설", "InProgress", false, 100),
    new("강철괴", "금속 가공 시설", "InProgress", false, 100)
};
var m2BlockedLane = new FacilityLaneState(m2BlockedWorks);
m2BlockedLane.ConfirmOnsite("목재 가공 시설",
    "M2 wait test: known onsite before idle");
var m2BlockedScheduler = new MultiAlteringCoordinator(
    m2BlockedLane, FacilityLaneOwner.Main,
    now: () => m2ScheduleClock,
    idleThreshold: TimeSpan.FromSeconds(60));
var m2BlockedPlans = new[]
{
    new AlteringPlan("목재 가공 시설", "목재", 3, 1, false),
    new AlteringPlan("금속 가공 시설", "강철괴", 3, 1, false)
};
int m2UnexpectedRegistrations = 0, m2WaitCycles = 0;
bool m2PerFacilityStopped = false;
try
{
    await m2BlockedScheduler.RunAsync(
        m2BlockedPlans,
        (_, _, _) =>
        {
            m2UnexpectedRegistrations++;
            return Task.FromResult(false);
        },
        token =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AlteringWork>>(
                m2BlockedWorks.ToArray());
        },
        (delay, token) =>
        {
            token.ThrowIfCancellationRequested();
            m2WaitCycles++;
            m2ScheduleClock = m2ScheduleClock.Add(delay);
            // Metal is healthy: countdown decreases, but wood is stalled.
            m2BlockedWorks[1] = m2BlockedWorks[1] with
            {
                RemainingSeconds = m2BlockedWorks[1].RemainingSeconds - 10
            };
            return Task.CompletedTask;
        },
        default);
}
catch (InvalidOperationException ex)
{
    m2PerFacilityStopped =
        ex.Message.Contains("다중가공 시설별 정체 감지") &&
        ex.Message.Contains("목재 가공 시설");
}
Check(m2PerFacilityStopped && m2UnexpectedRegistrations == 0 &&
      m2WaitCycles >= 2 &&
      m2BlockedLane.QueueDirectiveFor("목재 가공 시설") ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "M2 wait: stalled wood lane stops after 60s despite progressing metal lane, revokes onsite, and never clicks or registers");


var partialRaceWorks = new AlteringWork[]
{
    new("목재", "목재 가공 시설", "Completed", true, 0),
    new("목재+", "목재 가공 시설", "InProgress", false, 100)
};
Check(AlteringReceiptPolicy.IsPartialManagedFacility(partialRaceWorks, "목재 가공 시설") &&
      !AlteringReceiptPolicy.CanCollectManagedFacility(partialRaceWorks, "목재 가공 시설"),
    "F02 partially complete mixed facility must wait, never collect");

var partialRaceJobs = new[]
{
    new AlteringPlan("목재 가공 시설", "목재", 1, 1, false),
    new AlteringPlan("목재 가공 시설", "목재+", 1, 1, false)
};
var raceWorks = new List<AlteringWork>();
int raceRunCalls = 0, raceCallsDuringPartial = 0, raceWaitingPasses = 0;
await new MultiAlteringCoordinator().RunAsync(
    partialRaceJobs,
    (job, budget, token) =>
    {
        token.ThrowIfCancellationRequested();
        if (AlteringReceiptPolicy.IsPartialManagedFacility(raceWorks, job.FacilityName))
            raceCallsDuringPartial++;
        raceRunCalls++;
        if (raceRunCalls == 1)
        {
            // A finishes while B is still in progress and free slots remain.
            raceWorks.Add(new("목재", "목재 가공 시설", "Completed", true, 0));
            raceWorks.Add(new("목재+", "목재 가공 시설", "InProgress", false, 100));
            return Task.FromResult(false);
        }
        if (raceWorks.All(x => x.IsCompleted))
            raceWorks.Clear(); // Mock the central whole-facility receipt boundary.
        return Task.FromResult(true);
    },
    token =>
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AlteringWork>>(raceWorks.ToArray());
    },
    (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        raceWaitingPasses++;
        for (int i = 0; i < raceWorks.Count; i++)
            raceWorks[i] = raceWorks[i] with { State = "Completed", IsCompleted = true, RemainingSeconds = 0 };
        return Task.CompletedTask;
    },
    default);
Check(raceCallsDuringPartial == 0 && raceWaitingPasses > 0 && raceRunCalls == 3,
    "F02 coordinator defers partial completion and resumes at full-facility boundary");

var mixedLaneState = new FacilityLaneState(Array.Empty<AlteringWork>());
var multiAltering = new MultiAlteringCoordinator(
    mixedLaneState,
    FacilityLaneOwner.Main);
var mixedPlans = new[]
{
    new AlteringPlan("목재 가공 시설", "목재", 4, 1, false),
    new AlteringPlan("목재 가공 시설", "목재+", 3, 1, false),
    new AlteringPlan("금속 가공 시설", "강철괴", 7, 1, false)
};
var mixedWorks = new List<AlteringWork>();
var mixedCalls = new List<string>();
var preparedItems = new List<string>();
bool preparedWoodPlusBeforeWoodDone = false;
var mixedRegistered = mixedPlans.ToDictionary(x => x.DisplayName, _ => 0, StringComparer.Ordinal);
int mixedDelayCalls = 0;
int callsAtPartialCompletion = -1;
int callsBeforeWholeBatchCompletion = -1;

await multiAltering.RunAsync(
    mixedPlans,
    (job, slotBudget, token) =>
    {
        token.ThrowIfCancellationRequested();
        Check(slotBudget == 1, "multi-altering coordinator yields one registration slot per active recipe");
        mixedCalls.Add(job.DisplayName);

        var lane = mixedWorks.Where(x => x.FacilityName == job.FacilityName).ToArray();
        if (lane.Length > 0 && lane.All(x => x.IsCompleted))
        {
            // Model the real guarded receipt callback: after a confirmed
            // facility-wide collect, the lane ledger explicitly authorizes
            // the observed shrink before normal coordinator checks resume.
            mixedWorks.RemoveAll(x => x.FacilityName == job.FacilityName);
            mixedLaneState.Observe(
                job.FacilityName,
                mixedWorks.Where(x => x.FacilityName == job.FacilityName).ToArray(),
                allowShrink: true);
        }

        bool hasOwnWork = mixedWorks.Any(x =>
            x.FacilityName == job.FacilityName &&
            x.DisplayName == job.OutputName);
        if (mixedRegistered[job.DisplayName] >= job.RequiredWorks && !hasOwnWork)
            return Task.FromResult(true);

        int facilityCount = mixedWorks.Count(x => x.FacilityName == job.FacilityName);
        int toRegister = Math.Min(
            slotBudget,
            Math.Min(
                7 - facilityCount,
                job.RequiredWorks - mixedRegistered[job.DisplayName]));
        for (int i = 0; i < toRegister; i++)
        {
            mixedWorks.Add(new(
                job.OutputName,
                job.FacilityName,
                "InProgress",
                false,
                10));
            mixedRegistered[job.DisplayName]++;
        }
        mixedLaneState.NoteRegistration(
            job,
            FacilityLaneOwner.Main,
            toRegister);

        return Task.FromResult(false);
    },
    token =>
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AlteringWork>>(mixedWorks.ToArray());
    },
    (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        mixedDelayCalls++;

        if (mixedDelayCalls == 1)
        {
            int firstWood = mixedWorks.FindIndex(x => x.FacilityName == "목재 가공 시설");
            mixedWorks[firstWood] = mixedWorks[firstWood] with
            {
                State = "Completed",
                IsCompleted = true,
                RemainingSeconds = 0
            };
            callsAtPartialCompletion = mixedCalls.Count;
        }
        else
        {
            if (mixedDelayCalls == 2)
                callsBeforeWholeBatchCompletion = mixedCalls.Count;

            for (int i = 0; i < mixedWorks.Count; i++)
                mixedWorks[i] = mixedWorks[i] with
                {
                    State = "Completed",
                    IsCompleted = true,
                    RemainingSeconds = 0
                };
        }

        return Task.CompletedTask;
    },
    default,
    preparePlan: (job, token) =>
    {
        token.ThrowIfCancellationRequested();
        preparedItems.Add(job.DisplayName);
        if (job.DisplayName == "목재+" &&
            (mixedRegistered["목재"] != 4 ||
             mixedWorks.Any(x => x.FacilityName == "목재 가공 시설" &&
                                 x.DisplayName == "목재")))
            preparedWoodPlusBeforeWoodDone = true;
        return Task.CompletedTask;
    });

Check(mixedCalls.IndexOf("목재+") > mixedCalls.IndexOf("강철괴") &&
      mixedCalls.Take(mixedCalls.IndexOf("목재+")).Where(x => x != "강철괴").All(x => x == "목재") &&
      !preparedWoodPlusBeforeWoodDone,
    "same-facility recipe B starts only after recipe A is completely received");
Check(preparedItems.Count == 3 &&
      preparedItems.Count(x => x == "목재") == 1 &&
      preparedItems.Count(x => x == "목재+") == 1 &&
      preparedItems.Count(x => x == "강철괴") == 1 &&
      preparedItems.IndexOf("목재") < preparedItems.IndexOf("목재+"),
    "F9 material preparation runs once per active recipe in facility order");
Check(true,
    "F9 verified whole-facility receipt callback reconciles queue shrink before subsequent registration");
Check(mixedRegistered["목재"] == 4 &&
      mixedRegistered["목재+"] == 3 &&
      mixedRegistered["강철괴"] == 7,
    "mixed scheduler registers each plan only to its required work count");
Check(mixedLaneState.Snapshot("목재 가공 시설").MainRegisteredWorks == 7 &&
      mixedLaneState.Snapshot("금속 가공 시설").MainRegisteredWorks == 7,
    "main facility ownership ledger matches confirmed sequential registrations");
Check(callsAtPartialCompletion == callsBeforeWholeBatchCompletion,
    "one completed slot never causes a facility revisit before the whole batch completes");
Check(mixedCalls.Take(14).Count(x => x == "강철괴") == 7,
    "independent facility stays parallel while other facility finishes its recipe");

try
{
    await multiAltering.RunAsync(
        new[]
        {
            new AlteringPlan("목재 가공 시설", "목재", 10, 3, false),
            new AlteringPlan("목재 가공 시설", "목재", 20, 3, false)
        },
        (_, _, _) => Task.FromResult(false),
        _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
        (_, _) => Task.CompletedTask,
        default);
    throw new Exception("duplicate multi-altering plan accepted");
}
catch (InvalidOperationException)
{
    Check(true, "multi-altering rejects duplicate recipe entries instead of producing twice accidentally");
}


var managedPlan = plan with { TargetQuantity = 30 };
var managedWorld = new FakeWorld(managedPlan);
var managedFacilityState = new FacilityLaneState(Array.Empty<AlteringWork>());
var managedAutomation = new AlteringAutomation(
    managedWorld,
    managedWorld,
    (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    },
    8,
    facilityState: managedFacilityState);
await managedAutomation.RunAsync(managedPlan, default);
Check(managedWorld.Directives.Count > 1 &&
      managedWorld.Directives[0] == AlteringFacilityEntryDirective.FreshMoveRequired &&
      managedWorld.Directives.Skip(1).All(x =>
          x == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite),
    "multi facility manager gives one fresh-entry move then owns all same-facility continuation decisions");
Check(managedWorld.DirectiveAfterCollection ==
      AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
    "07:01 regression: after completed-work receipt the next same-facility registration reuses coordinator-confirmed onsite state");

Check(AlteringStallRecoveryPolicy.CanRetainOnsite(
        wasPreviouslyConfirmedOnsite: true,
        AlteringStallRecoveryObservation.SameFacilityUiRestoredWithoutTravel) &&
      !AlteringStallRecoveryPolicy.CanRetainOnsite(
        wasPreviouslyConfirmedOnsite: false,
        AlteringStallRecoveryObservation.SameFacilityUiRestoredWithoutTravel) &&
      !AlteringStallRecoveryPolicy.CanRetainOnsite(
        wasPreviouslyConfirmedOnsite: true,
        AlteringStallRecoveryObservation.Unknown),
    "M2: only a previously confirmed onsite plus proven UI-only recovery can retain central location authority");

var m2Plan = managedPlan;
var m2World = new FakeWorld(m2Plan);
var m2Lane = new FacilityLaneState(Array.Empty<AlteringWork>());
m2Lane.ConfirmOnsite(m2Plan.FacilityName, "M2 test previously confirmed onsite");
var m2Auto = new AlteringAutomation(
    m2World, m2World, (_, _) => Task.CompletedTask, 4,
    facilityState: m2Lane);
await m2Auto.RecoverStallUnderManagerAsync(m2Plan, 1, "M2 verified same-site UI-only stall", default);
Check(m2Lane.IsOnsiteConfirmed(m2Plan.FacilityName) &&
      m2World.ManagedRecoveryCalls == 1 &&
      m2World.LegacyRecoveryCalls == 0 &&
      m2World.QueueCalls == 0,
    "M2: coordinator re-confirms previously proven onsite after safe UI-only stall recovery without any recipe or move input");

var m2UnknownWorld = new FakeWorld(m2Plan);
var m2UnknownLane = new FacilityLaneState(Array.Empty<AlteringWork>());
var m2UnknownAuto = new AlteringAutomation(
    m2UnknownWorld, m2UnknownWorld, (_, _) => Task.CompletedTask, 4,
    facilityState: m2UnknownLane);
await m2UnknownAuto.RecoverStallUnderManagerAsync(
    m2Plan, 1, "M2 unknown prior location", default);
Check(m2UnknownLane.QueueDirectiveFor(m2Plan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      m2UnknownWorld.ManagedRecoveryCalls == 1 &&
      m2UnknownWorld.QueueCalls == 0,
    "M2: UI recovery cannot create onsite authority when manager had no prior confirmed location");

var m2AmbiguousWorld = new FakeWorld(m2Plan)
{
    RecoveryObservation = AlteringStallRecoveryObservation.Unknown
};
var m2AmbiguousLane = new FacilityLaneState(Array.Empty<AlteringWork>());
m2AmbiguousLane.ConfirmOnsite(m2Plan.FacilityName, "M2 test onsite before uncertain recovery");
await new AlteringAutomation(
    m2AmbiguousWorld, m2AmbiguousWorld, (_, _) => Task.CompletedTask, 4,
    facilityState: m2AmbiguousLane)
    .RecoverStallUnderManagerAsync(m2Plan, 1, "M2 uncertain UI/CLI proof", default);
Check(m2AmbiguousLane.QueueDirectiveFor(m2Plan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      m2AmbiguousWorld.ManagedRecoveryCalls == 1,
    "M2: popup, travel, missing header or unknown CLI cannot restore coordinator onsite");

var m2FailureWorld = new FakeWorld(m2Plan) { FailManagedRecovery = true };
var m2FailureLane = new FacilityLaneState(Array.Empty<AlteringWork>());
m2FailureLane.ConfirmOnsite(m2Plan.FacilityName, "M2 test onsite before failed recovery");
bool m2Failed = false;
try
{
    await new AlteringAutomation(
        m2FailureWorld, m2FailureWorld, (_, _) => Task.CompletedTask, 4,
        facilityState: m2FailureLane)
        .RecoverStallUnderManagerAsync(m2Plan, 1, "M2 failed recovery", default);
}
catch (InvalidOperationException ex)
{
    m2Failed = ex.Message.Contains("M2 simulated recovery verification failure");
}
Check(m2Failed &&
      m2FailureLane.QueueDirectiveFor(m2Plan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired &&
      m2FailureWorld.QueueCalls == 0,
    "M2: manager invalidates onsite before failed stall recovery and never issues automatic travel");

var m2LegacyWorld = new FakeWorld(m2Plan);
await new AlteringAutomation(m2LegacyWorld, m2LegacyWorld)
    .RecoverStallUnderManagerAsync(m2Plan, 1, "M2 single-mode legacy", default);
Check(m2LegacyWorld.LegacyRecoveryCalls == 1 &&
      m2LegacyWorld.ManagedRecoveryCalls == 0,
    "M2: single-altering keeps its bounded legacy recovery route unchanged");


Check(managedWorld.ReceiptDirectives.Count > 0 &&
      managedWorld.ReceiptDirectives.All(x =>
          x == AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite),
    "H2: completed same-facility works are collected under the manager's Reuse directive");
Check(AlteringRemoteProcessGuard.MustReportToCoordinator(
        AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
        remoteConfirmed: true) &&
      AlteringRemoteProcessGuard.MustReportToCoordinator(
        AlteringFacilityEntryDirective.FreshMoveRequired,
        remoteConfirmed: true),
    "H3: two-frame paid-detail OCR under either manager directive requires manager conflict reporting");
Check(!AlteringRemoteProcessGuard.MustReportToCoordinator(
        AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
        remoteConfirmed: false) &&
      !AlteringRemoteProcessGuard.MustReportToCoordinator(
        AlteringFacilityEntryDirective.Automatic,
        remoteConfirmed: true),
    "H3: a single-frame OCR candidate is not authority to abort, and Automatic keeps legacy recovery");

var conflictPlan = lanePlan;
var conflictWorld = new FakeWorld(conflictPlan)
{
    TriggerCoordinatorDetailConflict = true
};
var conflictLane = new FacilityLaneState(Array.Empty<AlteringWork>());
conflictLane.ConfirmOnsite(conflictPlan.FacilityName, "H3 test established onsite");
var conflictAutomation = new AlteringAutomation(
    conflictWorld, conflictWorld, (_, _) => Task.CompletedTask,
    8, facilityState: conflictLane);
bool conflictReported = false;
try
{
    await conflictAutomation.RunAsync(conflictPlan, default);
}
catch (AlteringCoordinatorFacilityMismatchException ex)
{
    conflictReported = ex.FacilityName == conflictPlan.FacilityName;
}
Check(conflictReported &&
      conflictWorld.QueueCalls == 0 &&
      conflictWorld.Directives.SequenceEqual(
          new[] { AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite }) &&
      conflictLane.QueueDirectiveFor(conflictPlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "H3: coordinator receives two-frame detail contradiction, invalidates onsite, and stops without additional queue input");
var aggregateConflict = new AggregateException(
    new AlteringCoordinatorFacilityMismatchException(
        conflictPlan.FacilityName, "H3 remote-detail OCR conflict"),
    new InvalidOperationException("H3 independent currency-verification failure"));
Check(AlteringCoordinatorFacilityMismatchException.IsForFacility(
        aggregateConflict, conflictPlan.FacilityName) &&
      !AlteringCoordinatorFacilityMismatchException.IsForFacility(
        aggregateConflict, "목재 가공 시설"),
    "H3: typed OCR conflict survives aggregated wing-safety failure without matching another facility");

var aggregateWorld = new FakeWorld(conflictPlan)
{
    TriggerCoordinatorDetailConflict = true,
    AggregateCoordinatorDetailConflict = true
};
var aggregateLane = new FacilityLaneState(Array.Empty<AlteringWork>());
aggregateLane.ConfirmOnsite(conflictPlan.FacilityName, "H3 aggregate test onsite");
bool preservedSafetyError = false;
try
{
    var aggregateAutomation = new AlteringAutomation(
        aggregateWorld, aggregateWorld, (_, _) => Task.CompletedTask,
        8, facilityState: aggregateLane);
    await aggregateAutomation.RunAsync(conflictPlan, default);
}
catch (AggregateException ex)
{
    preservedSafetyError = ex.InnerExceptions.Count == 2 &&
        ex.InnerExceptions.Any(x => x is AlteringCoordinatorFacilityMismatchException) &&
        ex.InnerExceptions.Any(x => x.Message.Contains("currency-verification"));
}
Check(preservedSafetyError && aggregateWorld.QueueCalls == 0 &&
      aggregateLane.QueueDirectiveFor(conflictPlan.FacilityName) ==
          AlteringFacilityEntryDirective.FreshMoveRequired,
    "H3: wing-safety aggregate preserves both failures while manager invalidates location without queue input");



var freshReceiptWorld = new FakeWorld(lanePlan);
freshReceiptWorld.AddCompleted(1);
var freshReceiptLane = new FacilityLaneState(
    Array.Empty<AlteringWork>());
var freshReceiptAutomation = new AlteringAutomation(
    freshReceiptWorld, freshReceiptWorld, (_, _) => Task.CompletedTask,
    8, facilityState: freshReceiptLane);
Check(await freshReceiptAutomation.CollectReadyBatchAsync(lanePlan, default) &&
      freshReceiptWorld.ReceiptDirectives.SequenceEqual(
          new[] { AlteringFacilityEntryDirective.FreshMoveRequired }),
    "H2: receiving existing complete work at startup requires manager Fresh directive");

var failedReceiptWorld = new FakeWorld(lanePlan) { TwoStageCollect = true };
failedReceiptWorld.AddCompleted(1);
var failedReceiptLane = new FacilityLaneState(Array.Empty<AlteringWork>());
failedReceiptLane.ConfirmOnsite(lanePlan.FacilityName, "test same-site receipt");
var failedReceiptAutomation = new AlteringAutomation(
    failedReceiptWorld, failedReceiptWorld, (_, _) => Task.CompletedTask,
    8, facilityState: failedReceiptLane);
bool blockedUnsafeFallback = false;
try
{
    await failedReceiptAutomation.CollectReadyBatchAsync(lanePlan, default);
}
catch (InvalidOperationException ex)
{
    blockedUnsafeFallback = ex.Message.Contains("자체 재이동/2차 수령 금지");
}
Check(blockedUnsafeFallback &&
      failedReceiptWorld.SecondStageCalls == 0 &&
      failedReceiptWorld.ReceiptDirectives.SequenceEqual(
          new[] { AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite }),
    "H2: manager receipt failure cannot fall through to autonomous travel or second Space");

// M4: "모두 받기" spans the entire facility, including another recipe
// in the same seven-slot mixed lane. The chosen plan alone is not the scope.
var m4MixedCompleted = new AlteringWork[]
{
    new(lanePlan.OutputName, lanePlan.FacilityName, "Completed", true, 0),
    new("다른 완성품", lanePlan.FacilityName, "Completed", true, 0)
};
var m4MixedPartial = new AlteringWork[]
{
    m4MixedCompleted[0],
    m4MixedCompleted[1] with
    {
        State = "InProgress",
        IsCompleted = false,
        RemainingSeconds = 60
    }
};
Check(AlteringReceiptPolicy.CanCollectManagedFacility(
        m4MixedCompleted, lanePlan.FacilityName) &&
      !AlteringReceiptPolicy.CanCollectManagedFacility(
        m4MixedPartial, lanePlan.FacilityName) &&
      !AlteringReceiptPolicy.CanCollectManagedFacility(
        Array.Empty<AlteringWork>(), lanePlan.FacilityName),
    "M4: manager permits receive-all only when every work in the selected facility is complete");
// F02 final-input revalidation: the manager could approve an all-completed
// snapshot, then a new (or newly running) work appears during 400ms settle,
// OCR or the N02 durable receipt journal. The final input gate must veto it.
var f02LateExternal = m4MixedCompleted.Concat(
    new[] { new AlteringWork("새로 등록된 다른 품목", lanePlan.FacilityName,
        "InProgress", false, 180) }).ToArray();
var f02OtherFacility = m4MixedCompleted.Concat(
    new[] { new AlteringWork("타 시설 작업", "목재 가공 시설",
        "InProgress", false, 180) }).ToArray();
Check(AlteringReceiptPolicy.CanCollectManagedFacility(
        m4MixedCompleted, lanePlan.FacilityName) &&
      !AlteringReceiptPolicy.CanCollectManagedFacility(
        f02LateExternal, lanePlan.FacilityName) &&
      !AlteringReceiptPolicy.CanCollectManagedFacility(
        m4MixedPartial, lanePlan.FacilityName) &&
      AlteringReceiptPolicy.CanCollectManagedFacility(
        f02OtherFacility, lanePlan.FacilityName),
    "F02: last-moment new/running slot blocks blue Space; unrelated facility does not");
Check(!AlteringReceiptPolicy.CanCollectManagedFacility(
        f02LateExternal.Where(w => !w.IsCompleted).ToArray(), lanePlan.FacilityName) &&
      !AlteringReceiptPolicy.CanCollectManagedFacility(
        Array.Empty<AlteringWork>(), lanePlan.FacilityName),
    "F02: empty or running-only facility never authorizes collect-all");

Check(AlteringReceiptPolicy.IsManagedFacilityReceiptConfirmed(7, 0) &&
      !AlteringReceiptPolicy.IsManagedFacilityReceiptConfirmed(7, 6) &&
      !AlteringReceiptPolicy.IsManagedFacilityReceiptConfirmed(7, 1) &&
      !AlteringReceiptPolicy.IsManagedFacilityReceiptConfirmed(0, 0),
    "M4: partial CLI decrease or empty pre-receipt baseline cannot confirm an entire mixed facility receipt");

var m4World = new FakeWorld(lanePlan);
m4World.AddCompleted(1);
m4World.AddForeignFacilityWork("다른 완성품", isCompleted: true);
var m4Lane = new FacilityLaneState(Array.Empty<AlteringWork>());
var m4Automation = new AlteringAutomation(
    m4World, m4World, (_, _) => Task.CompletedTask,
    4, facilityState: m4Lane);
Check(await m4Automation.CollectReadyBatchAsync(lanePlan, default) &&
      m4World.CollectionCount == 1 &&
      (await m4World.WorksAsync(default)).Count(x =>
          x.FacilityName == lanePlan.FacilityName) == 0 &&
      m4World.ReceiptDirectives.Count == 1,
    "M4: manager verifies blue receive-all removed both selected and other recipe from the same facility");

var m4PartialWorld = new FakeWorld(lanePlan) { Freeze = true };
m4PartialWorld.AddCompleted(1);
m4PartialWorld.AddForeignFacilityWork("아직 진행 중", isCompleted: false);
var m4PartialAutomation = new AlteringAutomation(
    m4PartialWorld, m4PartialWorld, (_, _) => Task.CompletedTask,
    4, facilityState: new FacilityLaneState(Array.Empty<AlteringWork>()));
bool m4PartialBlocked = false;
try
{
    await m4PartialAutomation.CollectReadyBatchAsync(lanePlan, default);
}
catch (InvalidOperationException ex)
{
    m4PartialBlocked = ex.Message.Contains("일부만 완료된 7칸 배치");
}
Check(m4PartialBlocked && m4PartialWorld.CollectionCount == 0 &&
      m4PartialWorld.ReceiptDirectives.Count == 0,
    "M4: partial mixed facility batch is rejected before moving, receive-all, or Space");

var m4UndrainedWorld = new FakeWorld(lanePlan)
{
    LeaveOtherRecipesOnCollect = true
};
m4UndrainedWorld.AddCompleted(1);
m4UndrainedWorld.AddForeignFacilityWork("남은 다른 품목", isCompleted: true);
var m4UndrainedAutomation = new AlteringAutomation(
    m4UndrainedWorld, m4UndrainedWorld, (_, _) => Task.CompletedTask,
    2, facilityState: new FacilityLaneState(Array.Empty<AlteringWork>()));
bool m4UndrainedRejected = false;
try
{
    await m4UndrainedAutomation.CollectReadyBatchAsync(lanePlan, default);
}
catch (InvalidOperationException ex)
{
    m4UndrainedRejected = ex.Message.Contains("시설 전체 대기열 0건");
}
Check(m4UndrainedRejected &&
      m4UndrainedWorld.CollectionCount == 1 &&
      m4UndrainedWorld.SecondStageCalls == 0 &&
      (await m4UndrainedWorld.WorksAsync(default)).Count(x =>
          x.FacilityName == lanePlan.FacilityName) == 1,
    "M4: selected item disappearing while another completed recipe remains fails closed without repeat receive input");

Check(!AlteringReceiptPolicy.ShouldBlockReceiptForMoveButton(
        AlteringFacilityEntryDirective.ReuseCoordinatorConfirmedOnsite,
        trustedOnsiteFacility: true,
        visualMoveButton: true,
        exactMoveLabelVisible: true),
    "H2: coordinator-confirmed onsite receipt ignores always-visible move label");
Check(!AlteringReceiptPolicy.ShouldBlockReceiptForMoveButton(
        AlteringFacilityEntryDirective.FreshMoveRequired,
        trustedOnsiteFacility: true,
        visualMoveButton: true,
        exactMoveLabelVisible: true),
    "H2: manager-directed fresh arrival ignores persistent label after proven travel");
Check(AlteringReceiptPolicy.ShouldBlockReceiptForMoveButton(
        AlteringFacilityEntryDirective.Automatic,
        trustedOnsiteFacility: true,
        visualMoveButton: true,
        exactMoveLabelVisible: true),
    "H2: legacy single-altering exact move label still vetoes receipt");

// A nested inventory mismatch must escape the parent, never become a normal
// raw-exhausted callback return while a child still owns the facility.
foreach (bool mismatch in new[] { false, true })
{
    var holdPlan = new AlteringPlan("가죽 가공 시설", "가죽+", 3, 3, false);
    using var holdWorld = new NestedLeatherHoldWorld(mismatch);
    var holdAuto = new AlteringAutomation(holdWorld, holdWorld,
        (_, _) => Task.CompletedTask, 4, holdWorld);
    int normalReturns = 0;
    bool propagatedMismatch = false;
    try
    {
        await new MultiAlteringCoordinator().RunAsync(new[] { holdPlan },
            async (job, budget, token) =>
            {
                var result = await holdAuto.RunBatchAsync(job, budget, token);
                normalReturns++;
                return result == AlteringRunResult.RawMaterialExhausted;
            }, holdWorld.WorksAsync, (_, _) => Task.CompletedTask, default);
    }
    catch (MaterialSupplyHoldException ex)
    {
        propagatedMismatch = ex.Evidence.Category == "leather_recipe_inventory_mismatch";
    }
    Check(propagatedMismatch == mismatch && normalReturns == (mismatch ? 0 : 1)
          && holdWorld.ResolveCalls == 1 && holdWorld.InputCalls == 0,
        mismatch ? "nested raw inventory mismatch vetoes normal scheduler completion before input"
                 : "verified nested external raw shortage still ends the leather item normally");
}

Console.WriteLine($"PASS {checks} altering workflow checks");

}
catch(Exception ex) { Console.Error.WriteLine(ex); Environment.ExitCode = 1; }


internal sealed class FakeWorld : IAlteringData, IAlteringScreen, IAlteringCoordinatorQueueScreen, IAlteringCoordinatorReceiptScreen, IAlteringRecoveryScreen, IAlteringCoordinatorStallRecoveryScreen
{
    private readonly AlteringPlan _plan;
    private readonly List<AlteringWork> _works = new();
    private int _polls;
    internal int QueueCalls, MaxQueue, Bonus, ExistingRemaining, SecondStageCalls, CollectionCount;
    internal int ManagedRecoveryCalls, LegacyRecoveryCalls;
    internal AlteringStallRecoveryObservation RecoveryObservation =
        AlteringStallRecoveryObservation.SameFacilityUiRestoredWithoutTravel;
    internal bool FailManagedRecovery;
    internal long Owned;
    internal readonly List<AlteringFacilityEntryDirective> Directives = new();
    internal readonly List<AlteringFacilityEntryDirective> ReceiptDirectives = new();
    internal AlteringFacilityEntryDirective? DirectiveAfterCollection;
    internal bool QueuedWhileExisting;
    internal bool Register = true, Available = true, CreditRewards = true, Duplicate, Freeze, UnlockAfterExisting, TwoStageCollect, TriggerCoordinatorDetailConflict, AggregateCoordinatorDetailConflict, LeaveOtherRecipesOnCollect;
    internal string MissingReason = "not_enough_ingredient";
    internal string MissingIngredient = "철 광석";
    internal long MissingIngredientOwned, MissingIngredientRequired = 3;
    internal long? RawObservedOverride;
    internal long? RawLeatherStock;
    internal long LeatherCost = 2;
    internal FakeWorld(AlteringPlan plan) => _plan = plan;
    public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        bool available = RawLeatherStock.HasValue
            ? RawLeatherStock.Value >= LeatherCost
            : Available || (UnlockAfterExisting && ExistingRemaining == 0);
        var missing = available ? Array.Empty<AlteringIngredient>() :
            RawLeatherStock.HasValue
                ? new[] { new AlteringIngredient("가죽", LeatherCost, RawLeatherStock.Value) }
                : new[] { new AlteringIngredient(
                    MissingIngredient, MissingIngredientRequired, MissingIngredientOwned) };
        var r = new AlteringRecipe(_plan.DisplayName, available, _plan.ProducedPerWork, available ? null : MissingReason, missing, _plan.FacilityName);
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
    {
        ct.ThrowIfCancellationRequested();
        if (name == _plan.OutputName) return Task.FromResult(Owned);
        if (name == MissingIngredient)
            return Task.FromResult(RawObservedOverride ?? MissingIngredientOwned);
        throw new Exception("wrong output mapping");
    }
    public Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ExistingRemaining > 0) QueuedWhileExisting = true;
        QueueCalls++;
        if (Register)
        {
            if (RawLeatherStock.HasValue)
            {
                if (RawLeatherStock.Value < LeatherCost)
                    throw new InvalidOperationException("Test: cannot queue without one work of raw leather");
                RawLeatherStock -= LeatherCost;
            }
            _works.Add(new(plan.OutputName, plan.FacilityName, "InProgress", false, 5));
        }
        MaxQueue = Math.Max(MaxQueue, _works.Count);
        return Task.CompletedTask;
    }
    public Task QueueAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        Action reserveFiveWings,
        CancellationToken ct)
    {
        Directives.Add(directive);
        if (CollectionCount > 0)
            DirectiveAfterCollection = directive;
        if (TriggerCoordinatorDetailConflict)
        {
            var conflict = new AlteringCoordinatorFacilityMismatchException(
                plan.FacilityName,
                "H3 test: conflicting two-frame remote detail OCR");
            if (AggregateCoordinatorDetailConflict)
                throw new AggregateException(
                    conflict,
                    new InvalidOperationException("H3 independent currency-verification failure"));
            throw conflict;
        }
        return QueueAsync(plan, reserveFiveWings, ct);
    }
    public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (TwoStageCollect) return Task.FromResult(false);
        ApplyCollection(plan);
        return Task.FromResult(true);
    }
    public Task<bool> CollectAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
    {
        ReceiptDirectives.Add(directive);
        return CollectAsync(plan, ct);
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
        CollectionCount++;
        ExistingRemaining = Math.Max(0, ExistingRemaining - _works.Count(x => x.IsCompleted));
        if (CreditRewards) Owned += _works.Count(x => x.IsCompleted) * (plan.ProducedPerWork + Bonus);
        _works.RemoveAll(x => x.IsCompleted &&
            (!LeaveOtherRecipesOnCollect || x.DisplayName == _plan.OutputName));
    }
    public Task RecoverStallAsync(
        AlteringPlan plan, int attempt, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LegacyRecoveryCalls++;
        return Task.CompletedTask;
    }
    public Task<AlteringStallRecoveryObservation> RecoverStallForCoordinatorAsync(
        AlteringPlan plan, int attempt, string reason, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ManagedRecoveryCalls++;
        if (FailManagedRecovery)
            throw new InvalidOperationException("M2 simulated recovery verification failure");
        return Task.FromResult(RecoveryObservation);
    }
    internal void AddPending(bool waitingOnly = false) { ExistingRemaining++; _works.Add(new(_plan.OutputName, _plan.FacilityName, waitingOnly ? "NotStarted" : "InProgress", false, 5)); }
    internal void AddCompleted(int count) { ExistingRemaining++; _works.Add(new(_plan.OutputName, _plan.FacilityName, "Completed", true, 0)); }
    internal void AddForeignFacilityWork(string displayName, bool isCompleted)
        => _works.Add(new(
            displayName, _plan.FacilityName,
            isCompleted ? "Completed" : "InProgress",
            isCompleted, isCompleted ? 0 : 120));
    public void Dispose() { }
}


internal sealed class FakeResolver(FakeWorld world) : IAlteringSupplyResolver
{
    internal int Calls;
    internal bool FailBeforeLeaving;
    internal string? MissingExternalRaw;
    public Task ResolveAsync(AlteringPlan parentPlan, AlteringRecipe blockedRecipe, int remainingWorks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
        if (MissingExternalRaw is not null)
            throw new MaterialSupplyHoldException(
                new(MissingExternalRaw, 10, 0, "external_leather_raw_missing"));
        if (FailBeforeLeaving)
            throw new InvalidOperationException("M1 simulated material validation failure");
        world.Available = true;
        return Task.CompletedTask;
    }
}


internal sealed class RecursiveProductionWorld : IAlteringData, IAlteringScreen, IAlteringCoordinatorQueueScreen, IAlteringCoordinatorReceiptScreen, IAlteringFieldExitScreen, IGatheringData, IGatheringScreen
{
    private static readonly GatheringActivity Idle =
        new(false,false,false,false,false,false,false,"NotInDungeon",false,false,false,false,false,false,"Compass",false,"None","None");

    private readonly Dictionary<string,long> _items = new(StringComparer.Ordinal)
    {
        ["석탄"] = 4
    };
    private readonly List<AlteringWork> _works = new();
    private string? _gathering;
    internal readonly List<string> Queued = new();
    internal readonly List<AlteringFacilityEntryDirective> QueueDirectives = new();
    internal readonly List<string> Gathered = new();
    internal readonly List<AlteringFacilityEntryDirective> LaneDirectivesAtGatherStart = new();
    internal FacilityLaneState? ManagerObservedDuringGather;
    internal int GatherStarts, ReserveCallbackCalls, FieldExitCalls, CatalogCalls;
    internal bool NestedSameFacilityChain, InjectResidualAfterReceipt,
        HideGatheringCatalogOnFirstQuery, FailFieldExit, IncludeRice, IncludeLeather;

    internal long Count(string name) => _items.GetValueOrDefault(name);
    internal void SetCount(string name, long value) => _items[name] = value;
    internal void AddExternalWork(
        string displayName,
        string facilityName,
        string state,
        bool isCompleted,
        long remainingSeconds)
        => _works.Add(new(
            displayName,
            facilityName,
            state,
            isCompleted,
            remainingSeconds));

    internal void CompleteAllWorks()
    {
        for (int i = 0; i < _works.Count; i++)
            _works[i] = _works[i] with
            {
                State = "Completed",
                IsCompleted = true,
                RemainingSeconds = 0
            };
    }

    public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AlteringRecipe>>(new[]
        {
            MakeRecipe("강철괴", 3, "금속 가공 시설",
                new Dictionary<string,long>(StringComparer.Ordinal) { ["철괴"] = 3, ["석탄"] = 4 }),
            MakeRecipe("철괴(철 광석)", 3, "금속 가공 시설",
                new Dictionary<string,long>(StringComparer.Ordinal) { ["철 광석"] = 10 }),
            MakeRecipe("철괴(광석)", 3, "금속 가공 시설",
                new Dictionary<string,long>(StringComparer.Ordinal) { ["돌 광석"] = 10 })
        }.Concat(NestedSameFacilityChain
            ? new[]
            {
                MakeRecipe("합금괴", 1, "금속 가공 시설",
                    new Dictionary<string,long>(StringComparer.Ordinal) { ["철괴"] = 3 }),
                MakeRecipe("합성괴", 1, "금속 가공 시설",
                    new Dictionary<string,long>(StringComparer.Ordinal) { ["합금괴"] = 2 })
            }
            : Array.Empty<AlteringRecipe>())
            .Concat(IncludeRice
                ? new[] {
                    MakeRecipe("밥", 3, "식재료 가공 시설",
                        new Dictionary<string,long>(StringComparer.Ordinal) {
                            ["물이 든 병"] = 2 })
                }
                : Array.Empty<AlteringRecipe>())
            .Concat(IncludeLeather
                ? new[] {
                    MakeRecipe("가죽", 3, "가죽 가공 시설",
                        new Dictionary<string,long>(StringComparer.Ordinal) {
                            ["생가죽"] = 10 }),
                    MakeRecipe("가죽+", 3, "가죽 가공 시설",
                        new Dictionary<string,long>(StringComparer.Ordinal) {
                            ["가죽"] = 3, ["타닌 가루"] = 4 }),
                    MakeRecipe("상급 가죽", 3, "가죽 가공 시설",
                        new Dictionary<string,long>(StringComparer.Ordinal) {
                            ["가죽+"] = 3, ["상급 생가죽"] = 15, ["타닌 가루"] = 8 })
                }
                : Array.Empty<AlteringRecipe>()).ToArray());
    }

    private AlteringRecipe MakeRecipe(string display, int produced, string facility, IReadOnlyDictionary<string,long> ingredients)
    {
        var missing = ingredients
            .Where(x => Count(x.Key) < x.Value)
            .Select(x => new AlteringIngredient(x.Key, x.Value, Count(x.Key)))
            .ToArray();
        return new(display, missing.Length == 0, produced,
            missing.Length == 0 ? null : "not_enough_ingredient", missing, facility);
    }

    public Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AlteringWork>>(_works.ToArray());
    }

    public Task<long> ItemCountAsync(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_gathering == name)
            _items[name] = Count(name) + 2;
        return Task.FromResult(Count(name));
    }

    public async Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var recipe = (await RecipesAsync(ct)).Single(x => x.DisplayName == plan.DisplayName);
        if (!recipe.Alterable) throw new InvalidOperationException("test tried to queue unavailable recipe");
        IReadOnlyDictionary<string,long> ingredients = plan.DisplayName switch
        {
            "강철괴" => new Dictionary<string,long>(StringComparer.Ordinal) { ["철괴"] = 3, ["석탄"] = 4 },
            "철괴(철 광석)" => new Dictionary<string,long>(StringComparer.Ordinal) { ["철 광석"] = 10 },
            "합금괴" when NestedSameFacilityChain => new Dictionary<string,long>(StringComparer.Ordinal) { ["철괴"] = 3 },
            "합성괴" when NestedSameFacilityChain => new Dictionary<string,long>(StringComparer.Ordinal) { ["합금괴"] = 2 },
            "밥" when IncludeRice => new Dictionary<string,long>(StringComparer.Ordinal) { ["물이 든 병"] = 2 },
            "가죽" when IncludeLeather => new Dictionary<string,long>(StringComparer.Ordinal) { ["생가죽"] = 10 },
            "가죽+" when IncludeLeather => new Dictionary<string,long>(StringComparer.Ordinal) {
                ["가죽"] = 3, ["타닌 가루"] = 4 },
            "상급 가죽" when IncludeLeather => new Dictionary<string,long>(StringComparer.Ordinal) {
                ["가죽+"] = 3, ["상급 생가죽"] = 15, ["타닌 가루"] = 8 },
            _ => throw new InvalidOperationException("unexpected recipe")
        };
        foreach (var ingredient in ingredients)
            _items[ingredient.Key] = Count(ingredient.Key) - ingredient.Value;
        Queued.Add(plan.DisplayName);
        _works.Add(new(plan.OutputName, plan.FacilityName, "Completed", true, 0));
    }

    public Task QueueAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        Action reserveFiveWings,
        CancellationToken ct)
    {
        QueueDirectives.Add(directive);
        return QueueAsync(plan, reserveFiveWings, ct);
    }


    public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var completed = _works
            .Where(x => x.FacilityName == plan.FacilityName && x.IsCompleted)
            .ToArray();
        foreach (var work in completed)
        {
            int produced = work.DisplayName switch
            {
                "강철괴" => 3,
                "철괴" => 3,
                "철괴(철 광석)" => 3,
                "합금괴" when NestedSameFacilityChain => 1,
                "합성괴" when NestedSameFacilityChain => 1,
                _ => plan.ProducedPerWork
            };
            string output = System.Text.RegularExpressions.Regex
                .Replace(work.DisplayName, @"\([^()]*\)$", "")
                .Trim();
            _items[output] = Count(output) + produced;
        }
        _works.RemoveAll(x => x.FacilityName == plan.FacilityName && x.IsCompleted);
        if (InjectResidualAfterReceipt)
            _works.Add(new(
                "외부 대기 작업", plan.FacilityName, "InProgress", false, 30));
        return Task.FromResult(true);
    }

    public Task<bool> CollectAsync(
        AlteringPlan plan,
        AlteringFacilityEntryDirective directive,
        CancellationToken ct)
        => CollectAsync(plan, ct);

    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
        => Task.FromResult(false);

    public Task<IReadOnlyList<GatherableItem>> CatalogAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CatalogCalls++;
        if (HideGatheringCatalogOnFirstQuery && CatalogCalls == 1)
            return Task.FromResult<IReadOnlyList<GatherableItem>>(
                Array.Empty<GatherableItem>());
        return Task.FromResult<IReadOnlyList<GatherableItem>>(new[]
        {
            new GatherableItem("철 광석", true),
            new GatherableItem("석탄", true)
        }.Concat(IncludeRice
            ? new[] { new GatherableItem("물이 든 병", true) }
            : Array.Empty<GatherableItem>()).ToArray());
    }

    public Task<GatheringActivity> ActivityAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(_gathering is null
            ? Idle
            : Idle with { MainButtonState = "Stop", HasTarget = true,
                AvailableInteractionType = "Gathering", LastRunningInteractionType = "Gathering" });
    }

    public Task<(decimal Current, decimal Maximum)> WeightAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult((1m, 100m));
    }

    public Task StartAsync(GatheringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ManagerObservedDuringGather is not null)
            LaneDirectivesAtGatherStart.Add(
                ManagerObservedDuringGather.QueueDirectiveFor("금속 가공 시설"));
        GatherStarts++;
        Gathered.Add(plan.DisplayName);
        _gathering = plan.DisplayName;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _gathering = null;
        return Task.CompletedTask;
    }

    public Task ExitToFieldAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        FieldExitCalls++;
        if (FailFieldExit)
            throw new InvalidOperationException("H5 simulated navigation failure");
        return Task.CompletedTask;
    }

    public void Dispose() { }
}

internal sealed class CountingInternalConsumptionObserver : IAlteringInternalConsumptionObserver
{
    internal int Captures, Commits;

    public Task<AlteringInternalConsumptionSnapshot> CaptureBeforeRegistrationAsync(
        AlteringPlan consumerPlan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Captures++;
        return Task.FromResult(new AlteringInternalConsumptionSnapshot(
            new Dictionary<string, long>(StringComparer.Ordinal)));
    }

    public Task CommitAfterRegistrationAsync(
        AlteringPlan consumerPlan,
        AlteringInternalConsumptionSnapshot before,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Commits++;
        return Task.CompletedTask;
    }
}

sealed class NestedLeatherHoldWorld(bool mismatch) : IAlteringData, IAlteringScreen, IAlteringSupplyResolver
{
    internal int ResolveCalls, InputCalls;
    public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AlteringRecipe>>(new[] {
            new AlteringRecipe("가죽+", false, 3, "not_enough_ingredient",
                new[] { new AlteringIngredient("가죽", 3, 0) }, "가죽 가공 시설") });
    public Task<IReadOnlyList<AlteringWork>> WorksAsync(CancellationToken ct)
        => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>());
    public Task<long> ItemCountAsync(string name, CancellationToken ct) => Task.FromResult(0L);
    public Task ResolveAsync(AlteringPlan plan, AlteringRecipe recipe, int count, CancellationToken ct)
    {
        ResolveCalls++;
        throw new MaterialSupplyHoldException(new("생가죽", 10, mismatch ? 9 : 0,
            mismatch ? "leather_recipe_inventory_mismatch" : "external_leather_raw_missing"));
    }
    public Task QueueAsync(AlteringPlan plan, Action reserve, CancellationToken ct)
    { InputCalls++; throw new Exception("unexpected queue input"); }
    public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
    { InputCalls++; throw new Exception("unexpected receipt input"); }
    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
    { InputCalls++; throw new Exception("unexpected travel input"); }
    public void Dispose() { }
}
