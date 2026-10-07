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

Check(AlteringFacilityTravelConfirmPolicy.ShouldConfirm(
        greenConfirmationVisible: true,
        travelDialogVisible: true,
        confirmationAlreadySent: false),
    "facility travel dialog receives one Space confirmation when it actually appears");
Check(!AlteringFacilityTravelConfirmPolicy.ShouldConfirm(
        greenConfirmationVisible: false,
        travelDialogVisible: false,
        confirmationAlreadySent: false),
    "direct facility travel without a confirmation dialog never receives Space");
Check(!AlteringFacilityTravelConfirmPolicy.ShouldConfirm(
        greenConfirmationVisible: true,
        travelDialogVisible: false,
        confirmationAlreadySent: false),
    "generic green modal is never enough to authorize facility travel Space");
Check(!AlteringFacilityTravelConfirmPolicy.ShouldConfirm(
        greenConfirmationVisible: true,
        travelDialogVisible: true,
        confirmationAlreadySent: true),
    "facility travel confirmation is strictly one-shot");
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
    "16:43 arrival regression: two-frame/short move-button disappearance cannot authorize facility input");
Check(AlteringFacilityTravelConfirmPolicy.HasStableOnsiteEvidence(
        AlteringFacilityTravelConfirmPolicy.RequiredOnsiteStableFrames,
        TimeSpan.FromSeconds(3)),
    "facility arrival requires the full stable frame and duration threshold");
Check(AlteringFacilityTravelConfirmPolicy.FinalOnsiteRecheckDelay >= TimeSpan.FromSeconds(1),
    "facility arrival keeps a delayed final recheck after the stable window");

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
if (Directory.Exists(satisfiedSessionDir))
    Directory.Delete(satisfiedSessionDir, recursive: true);

var nestedProductionWorld = new RecursiveProductionWorld
{
    NestedSameFacilityChain = true
};
nestedProductionWorld.SetCount("철괴", 3); // Enough for one 합금괴, not both.
nestedProductionWorld.SetCount("철 광석", 0);
string nestedSessionDir = Path.Combine(
    Path.GetTempPath(), "mabi-nested-dependency-" + Guid.NewGuid().ToString("N"));
var nestedProductionLane = new FacilityLaneState(Array.Empty<AlteringWork>());
var nestedProductionScheduler = new MultiAlteringDependencyScheduler(
    nestedProductionWorld, nestedProductionWorld, testIdentity, nestedSessionDir,
    (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4, laneState: nestedProductionLane);
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
if (Directory.Exists(nestedSessionDir))
    Directory.Delete(nestedSessionDir, recursive: true);

if (Directory.Exists(dependencySessionDir))
    Directory.Delete(dependencySessionDir, recursive: true);

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

var wholePlanWorld = new RecursiveProductionWorld();
wholePlanWorld.SetCount("석탄", 0);
wholePlanWorld.SetCount("철 광석", 0);
var wholePlanResolver = new RecursiveAlteringSupplyResolver(
    wholePlanWorld, wholePlanWorld, wholePlanWorld, wholePlanWorld,
    delay: (_, token) => { token.ThrowIfCancellationRequested(); return Task.CompletedTask; },
    verificationAttempts: 4);
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
var mixedRegistered = mixedPlans.ToDictionary(x => x.DisplayName, _ => 0, StringComparer.Ordinal);
int mixedDelayCalls = 0;
int callsAtPartialCompletion = -1;
int callsBeforeWholeBatchCompletion = -1;

await multiAltering.RunAsync(
    mixedPlans,
    (job, slotBudget, token) =>
    {
        token.ThrowIfCancellationRequested();
        Check(slotBudget == 1, "multi-altering coordinator yields one registration slot per mixed turn");
        mixedCalls.Add(job.DisplayName);

        var lane = mixedWorks.Where(x => x.FacilityName == job.FacilityName).ToArray();
        if (lane.Length > 0 && lane.All(x => x.IsCompleted))
            mixedWorks.RemoveAll(x => x.FacilityName == job.FacilityName);

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
    default);

Check(mixedCalls.Take(7).SequenceEqual(
        new[] { "목재", "목재+", "목재", "목재+", "목재", "목재+", "목재" }),
    "same-facility plans are round-robin mixed across the seven-slot lane");
Check(mixedRegistered["목재"] == 4 &&
      mixedRegistered["목재+"] == 3 &&
      mixedRegistered["강철괴"] == 7,
    "mixed scheduler registers each plan only to its required work count");
Check(mixedLaneState.Snapshot("목재 가공 시설").MainRegisteredWorks == 7 &&
      mixedLaneState.Snapshot("금속 가공 시설").MainRegisteredWorks == 7,
    "main facility ownership ledger matches confirmed round-robin registrations");
Check(callsAtPartialCompletion == callsBeforeWholeBatchCompletion,
    "one completed slot never causes a facility revisit before the whole mixed batch completes");
Check(mixedCalls.Take(14).Count(x => x == "강철괴") == 7,
    "independent facility is fully seeded in parallel before waiting");

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
    internal bool NestedSameFacilityChain;

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
