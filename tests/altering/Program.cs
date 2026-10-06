using FishingAutomation;

try
{
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
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
Check(AlteringReceiptPolicy.CanConfirmCompletion(true, false, false, false),
    "real altering completion result can authorize confirmation");
Check(!AlteringReceiptPolicy.CanConfirmCompletion(true, false, true, false),
    "facility travel confirmation dialog never authorizes receipt Space");
Check(!AlteringReceiptPolicy.CanConfirmCompletion(true, false, false, true),
    "AutoTraveling state never authorizes completion Space");
Check(!AlteringReceiptPolicy.CanConfirmCompletion(true, true, false, false),
    "facility screen itself is not a completion result");

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
      AlteringRecipeLayout.IsSafeMedicineSearchGeometry(),
    "medicine uses crafting-style search instead of fixed recipe cards");
Check(AlteringFacilityLayout.IsSafeMoveGeometry() &&
      AlteringFacilityLayout.MoveButtonPoint == new System.Drawing.Point(85, 235) &&
      AlteringFacilityLayout.MoveButtonVisualArea == new System.Drawing.Rectangle(15, 205, 155, 65) &&
      AlteringFacilityLayout.OnsiteCloseVisualArea == new System.Drawing.Rectangle(744, 42, 44, 44),
    "facility move uses user-confirmed remote button and on-site close-X geometry");
Check(!AlteringFacilityLayout.ShouldAcceptMoveButton(true, true),
    "on-site close X vetoes a strong blue receive-control false move match");
Check(AlteringFacilityLayout.ShouldAcceptMoveButton(false, true),
    "real remote move shape remains accepted when the on-site close X is absent");

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
      remoteStatus.Contains("철괴(철 광석): 21/100 완료 · 남은시간 1분 30초") &&
      remoteStatus.Contains("목재: 50/100 완료 · 남은시간 계산 중") &&
      remoteStatus.Contains("물에 불린 쌀: 100/100 완료 · 남은시간 완료"),
    "processing status shows per-item completed/target counts and live remaining time");

var estimatePlan = plan with { TargetQuantity = 10 };
var estimateRecipe = new AlteringRecipe("강철괴", false, 3, "not_enough_ingredient",
    new[] { new AlteringIngredient("철괴", 2, 3) }, plan.FacilityName);
string estimate = AlteringMaterialEstimate.Describe(estimatePlan, estimateRecipe);
Check(estimate.Contains("철괴 예상 8") && estimate.Contains("부족 약 5"),
    "material preview projects remaining-work ingredient shortage without inventing hidden materials");

string sessionPath = Path.Combine(Path.GetTempPath(), "mabi-altering-" + Guid.NewGuid().ToString("N") + ".json");
var sessionStore = new AlteringSessionStore(sessionPath);
var testIdentity = new CliIdentityContext("char-1", "테스트", "account-1", "서버A");
var persisted = AlteringSessionState.Create(plan, testIdentity, 123, 2) with
{
    QueuedWorks = 5,
    Stage = "재료 해결 · 철괴"
};
sessionStore.Save(persisted);
var loadedSession = sessionStore.Load();
Check(loadedSession is not null && loadedSession.MatchesPlan(plan) &&
      loadedSession.MatchesIdentity(testIdentity) &&
      loadedSession.QueuedWorks == 5 && loadedSession.BaselineQuantity == 123 &&
      loadedSession.LastObservedOutputQuantity == 123 && loadedSession.Stage.Contains("철괴"),
    "altering session persists plan, identity, baseline, progress, and recursive stage");
sessionStore.Delete();
Check(!File.Exists(sessionPath), "completed session cleanup removes persisted resume state");
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

var multiWorld = new RecursiveProductionWorld();
multiWorld.SetCount("석탄", 0);
var multiResolver = new RecursiveAlteringSupplyResolver(
    multiWorld, multiWorld, multiWorld, multiWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
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

var multiAltering = new MultiAlteringCoordinator();
var multiPlans = new[]
{
    new AlteringPlan("금속 가공 시설", "철괴(철 광석)", 21, 3, false),
    new AlteringPlan("목재 가공 시설", "목재", 21, 3, false),
    new AlteringPlan("식재료 가공 시설", "물에 불린 쌀", 35, 5, false)
};
var multiWorks = new List<AlteringWork>();
var multiBatchRuns = new List<string>();
int multiDelayCalls = 0;
int runsAtFirstPartialCompletion = -1;

await multiAltering.RunAsync(
    multiPlans,
    (job, token) =>
    {
        token.ThrowIfCancellationRequested();
        multiBatchRuns.Add(job.DisplayName);
        var lane = multiWorks.Where(x => x.FacilityName == job.FacilityName).ToArray();
        if (lane.Length > 0 && lane.All(x => x.IsCompleted))
        {
            multiWorks.RemoveAll(x => x.FacilityName == job.FacilityName);
            return Task.FromResult(true);
        }

        if (lane.Length != 0)
            throw new Exception("multi-altering revisited a facility before its whole batch completed");

        for (int i = 0; i < 7; i++)
            multiWorks.Add(new(job.OutputName, job.FacilityName, i == 0 ? "InProgress" : "NotStarted", false, 10 + i));
        return Task.FromResult(false);
    },
    token =>
    {
        token.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AlteringWork>>(multiWorks.ToArray());
    },
    (_, token) =>
    {
        token.ThrowIfCancellationRequested();
        multiDelayCalls++;
        if (multiDelayCalls == 1)
        {
            int first = multiWorks.FindIndex(x => x.FacilityName == "금속 가공 시설");
            multiWorks[first] = multiWorks[first] with
            {
                State = "Completed",
                IsCompleted = true,
                RemainingSeconds = 0
            };
            runsAtFirstPartialCompletion = multiBatchRuns.Count;
        }
        else
        {
            for (int i = 0; i < multiWorks.Count; i++)
                multiWorks[i] = multiWorks[i] with
                {
                    State = "Completed",
                    IsCompleted = true,
                    RemainingSeconds = 0
                };
        }
        return Task.CompletedTask;
    },
    default);

Check(multiBatchRuns.Take(3).SequenceEqual(
        new[] { "철괴(철 광석)", "목재", "물에 불린 쌀" }),
    "multi-altering seeds every independent facility before waiting");
Check(runsAtFirstPartialCompletion == 3 &&
      multiBatchRuns.Count == 6,
    "multi-altering does not revisit/refill a facility for a single completed slot and revisits only whole completed batches");

try
{
    await multiAltering.RunAsync(
        new[]
        {
            new AlteringPlan("목재 가공 시설", "목재", 10, 3, false),
            new AlteringPlan("목재 가공 시설", "목재", 20, 3, false)
        },
        (_, _) => Task.FromResult(false),
        _ => Task.FromResult<IReadOnlyList<AlteringWork>>(Array.Empty<AlteringWork>()),
        (_, _) => Task.CompletedTask,
        default);
    throw new Exception("duplicate multi-altering plan accepted");
}
catch (InvalidOperationException)
{
    Check(true, "multi-altering rejects duplicate recipe entries instead of producing twice accidentally");
}

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
    internal bool QueuedWhileExisting;
    internal bool Register = true, Available = true, CreditRewards = true, Duplicate, Freeze, UnlockAfterExisting, TwoStageCollect;
    internal string MissingReason = "not_enough_ingredient";
    internal FakeWorld(AlteringPlan plan) => _plan = plan;
    public Task<IReadOnlyList<AlteringRecipe>> RecipesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        bool available = Available || (UnlockAfterExisting && ExistingRemaining == 0);
        var missing = available ? Array.Empty<AlteringIngredient>() : new[] { new AlteringIngredient("철 광석", 3, 0) };
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
    { ct.ThrowIfCancellationRequested(); if (name != _plan.OutputName) throw new Exception("wrong output mapping"); return Task.FromResult(Owned); }
    public Task QueueAsync(AlteringPlan plan, Action reserveFiveWings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ExistingRemaining > 0) QueuedWhileExisting = true;
        QueueCalls++;
        if (Register) _works.Add(new(plan.OutputName, plan.FacilityName, "InProgress", false, 5));
        MaxQueue = Math.Max(MaxQueue, _works.Count);
        return Task.CompletedTask;
    }
    public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (TwoStageCollect) return Task.FromResult(false);
        ApplyCollection(plan);
        return Task.FromResult(true);
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


internal sealed class RecursiveProductionWorld : IAlteringData, IAlteringScreen, IAlteringFieldExitScreen, IGatheringData, IGatheringScreen
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
    internal readonly List<string> Gathered = new();
    internal int GatherStarts, ReserveCallbackCalls, FieldExitCalls;

    internal long Count(string name) => _items.GetValueOrDefault(name);
    internal void SetCount(string name, long value) => _items[name] = value;

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
        });
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
            _ => throw new InvalidOperationException("unexpected recipe")
        };
        foreach (var ingredient in ingredients)
            _items[ingredient.Key] = Count(ingredient.Key) - ingredient.Value;
        Queued.Add(plan.DisplayName);
        _works.Add(new(plan.OutputName, plan.FacilityName, "Completed", true, 0));
    }

    public Task<bool> CollectAsync(AlteringPlan plan, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        int completed = _works.Count(x => x.FacilityName == plan.FacilityName && x.IsCompleted);
        if (completed > 0)
            _items[plan.OutputName] = Count(plan.OutputName) + (long)completed * plan.ProducedPerWork;
        _works.RemoveAll(x => x.FacilityName == plan.FacilityName && x.IsCompleted);
        return Task.FromResult(true);
    }

    public Task<bool> CollectAfterTravelAsync(AlteringPlan plan, CancellationToken ct)
        => Task.FromResult(false);

    public Task<IReadOnlyList<GatherableItem>> CatalogAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<GatherableItem>>(new[]
        {
            new GatherableItem("철 광석", true),
            new GatherableItem("석탄", true)
        });
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
        return Task.CompletedTask;
    }

    public void Dispose() { }
}
