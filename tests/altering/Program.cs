using FishingAutomation;

try
{
int checks = 0;
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
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

Console.WriteLine($"PASS {checks} altering workflow checks");

}
catch(Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }

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
    public Task ResolveAsync(AlteringPlan parentPlan, AlteringRecipe blockedRecipe, int remainingWorks, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Calls++;
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
        HideGatheringCatalogOnFirstQuery, FailFieldExit;

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
