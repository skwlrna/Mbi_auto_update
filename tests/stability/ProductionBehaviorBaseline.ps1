param(
    [string]$Root = (Resolve-Path (Join-Path $PSScriptRoot "../..")).Path
)

$ErrorActionPreference = 'Stop'
$checks = 0

function Read-Source([string]$relative) {
    $path = Join-Path $Root $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "stable behavior source missing: $relative" }
    return Get-Content -LiteralPath $path -Raw
}

function Require([bool]$ok, [string]$label) {
    if (-not $ok) { throw "STABILITY LOCK FAILED: $label" }
    $script:checks++
    Write-Host "PASS $label"
}

function Match-Required([string]$text, [string]$pattern, [string]$label) {
    Require ($text -match $pattern) $label
}

function Match-Forbidden([string]$text, [string]$pattern, [string]$label) {
    Require ($text -notmatch $pattern) $label
}

function Method-Block([string]$text, [string]$startPattern, [string]$nextPattern, [string]$label) {
    $m = [regex]::Match($text, "$startPattern[\s\S]*?(?=$nextPattern)")
    if (-not $m.Success) { throw "STABILITY LOCK FAILED: cannot isolate $label" }
    return $m.Value
}

$multiGather = Read-Source 'source/FishingAutomation/MultiGatheringCoordinator.cs'
$multiAlter = Read-Source 'source/FishingAutomation/MultiAlteringCoordinator.cs'
$multiAlterDependency = Read-Source 'source/FishingAutomation/MultiAlteringDependencyScheduler.cs'
$alterPlan = Read-Source 'source/FishingAutomation/AlteringPlan.cs'
$lane = Read-Source 'source/FishingAutomation/FacilityLaneState.cs'
$gathering = Read-Source 'source/FishingAutomation/GatheringAutomation.cs'
$bulk = Read-Source 'source/FishingAutomation/dungeon/InventoryBulkGatheringScreen.cs'
$stopPolicy = Read-Source 'source/FishingAutomation/LifeSkillStopPolicy.cs'
$navPolicy = Read-Source 'source/FishingAutomation/GatheringNavigationPolicy.cs'
$alter = Read-Source 'source/FishingAutomation/dungeon/AlteringScreen.cs'
$receiptPolicy = Read-Source 'source/FishingAutomation/AlteringReceiptPolicy.cs'
$travelPolicy = Read-Source 'source/FishingAutomation/AlteringFacilityTravelConfirmPolicy.cs'
$gatherTests = Read-Source 'tests/gathering/Program.cs'
$alterTests = Read-Source 'tests/altering/Program.cs'
$zeroWing = Read-Source 'source/FishingAutomation/ZeroWingScreenGuards.cs'

# 1) Coordinators schedule verified engines only. They never acquire direct-input authority.
Match-Required $multiGather 'new GatheringAutomation\(' 'multi-gather delegates every material to GatheringAutomation'
Match-Required $multiGather '시작 직전 재고' 'multi-gather rechecks inventory immediately before each material'
Match-Forbidden $multiGather 'TapFresh|ClickFresh|DragFresh|SendInput|InterceptionInput|0x39' 'multi-gather owns no direct input or Space'
Match-Required $multiAlter 'facilityWorks\.All\(x => x\.IsCompleted\)' 'multi-altering waits for whole facility batch completion'
Match-Required $multiAlter 'runBatch\(plan, 1, ct\)' 'same-facility work remains one-slot round-robin'
Match-Required $multiAlter '같은 시설 여러 품목은 라운드로빈 혼합' 'round-robin mixed-facility behavior remains explicit'
Match-Required $multiAlter '배치 전체 완료 전 이동 없음' 'partial slot completion never causes a facility move'
Match-Forbidden $multiAlter 'TapFresh|ClickFresh|DragFresh|SendInput|InterceptionInput|ProductionUiRuntime|0x39' 'multi-altering coordinator owns no direct UI input'

# 1b) V3.1.49 speed-up changes polling only; input order/coordinates stay owned by existing guards.
Match-Required $alterPlan 'VerifyRegistrationAsync' 'registration uses dedicated fast-then-safe verification'
Match-Required $alterPlan 'TimeSpan\.FromMilliseconds\(250\)' 'registration fast poll is 250ms'
Match-Required $alterPlan 'TimeSpan\.FromSeconds\(1\)' 'registration verification falls back to the proven 1s poll'
Match-Required $alter 'WaitForScreenStateAsync' 'processing navigation uses bounded screen-state polling'
Match-Required $alter 'WaitForProcessingNavigationReadyAsync' 'K/menu navigation can advance as soon as the expected screen appears'
Match-Required $multiAlter 'Math\.Clamp\(waitSeconds, 1, 30\)' 'multi-altering batch completion minimum poll is 1s'
Match-Required $multiAlterDependency 'Math\.Clamp\(waitSeconds, 1, 30\)' 'dependency batch completion minimum poll is 1s'

# 1c) V3.1.50 receipt input waits for the visible blue button to become input-stable.
Match-Required $alter '파란 수령 버튼 입력 전 400ms 안정화' 'receipt waits 400ms after the proven blue collect button'
Match-Required $alter 'Task\.Delay\(400, ct\)' 'receipt stabilization delay remains 400ms'
Match-Required $alter 'WaitForCollectPromptAsync\(plan, attempts: 2, delayMs: 100, ct, directive\)' 'receipt state is re-proven immediately before Space under manager directive'
Match-Required $alter '현장 수령 화면 재확인 완료' 'receipt Space is emitted only after the post-delay recheck'
Match-Required $alter 'Space 1회' 'receipt input remains a single Space after stabilization'

# 1d) V3.1.51 receipt state is separated from the legacy teal move-button detector.
Match-Required $alter 'receiptMode = false' 'facility travel exposes an explicit receipt-only state mode'
Match-Required $alter 'receiptMode: true' 'receipt callers opt into receipt-only travel state handling'
Match-Required $alter 'IsRemoteMoveButtonAfterReceiptMove' 'post-move receipt travel uses strict remote evidence'
Match-Required $alter 'ShouldBlockReceiptForMoveButton' 'receive Space uses trusted onsite state instead of teal-only veto'
Match-Required $receiptPolicy 'visualMoveButton && exactMoveLabelVisible' 'post-move teal shape alone cannot prove remote during receipt'
Match-Required $receiptPolicy 'visualMoveButton && !trustedOnsiteFacility' 'legacy visual veto remains conservative before onsite proof'
Match-Required $alterTests '04:05 receipt regression' '04:05 teal false-positive receipt failure remains executable coverage'

# 1e) V3.1.52: the multi-altering facility manager owns location decisions.
Match-Required $lane 'QueueDirectiveFor' 'facility manager issues the next facility-entry directive'
Match-Required $lane 'ConfirmOnsite' 'facility manager records proven same-facility onsite state'
Match-Required $lane 'InvalidateOnsite' 'facility manager explicitly clears onsite authority on departures/recovery'
Match-Required $lane 'ReuseCoordinatorConfirmedOnsite' 'same-facility continuation is a coordinator decision'
Match-Required $alterPlan '_facilityState.QueueDirectiveFor' 'multi-altering automation requests facility decisions from the manager'
Match-Required $alterPlan '완료품 수령 후 같은 시설창 복귀 확인' 'receipt completion returns onsite authority to the manager'
Match-Required $alter 'IAlteringCoordinatorQueueScreen' 'altering screen exposes a coordinator-command execution path'
Match-Required $alter '설비 이동 버튼 색상/형태 재판정 없음' 'coordinator-confirmed reuse cannot be downgraded by teal move-button heuristics'
Match-Required $zeroWing 'IAlteringCoordinatorQueueScreen' 'zero-wing safety wrapper forwards coordinator facility commands'
Match-Required $alterPlan 'IAlteringCoordinatorReceiptScreen' 'manager receipt interface exists'
Match-Required $alterPlan '_facilityState\.QueueDirectiveFor' 'manager decides receipt travel before invocation'
Match-Required $alterPlan '자체 재이동/2차 수령 금지' 'manager-led receipt failure cannot trigger autonomous fallback'
Match-Required $alter '중간관리자 수령 지시' 'receipt screen implements coordinator movement directive'
Match-Required $alter '설비 이동 0회' 'same-facility receipt cannot send a second travel command'
Match-Required $receiptPolicy 'Alterin[g]?FacilityEntryDirective' 'receipt policy recognizes manager instruction'
Match-Required $zeroWing 'IAlteringCoordinatorReceiptScreen' 'wing-safety wrapper preserves receipt directive'
Match-Required $alterTests '07:01 regression' '07:01 repeated facility-move regression remains executable coverage'
# H3: detailed paid-action OCR can be contradictory evidence, not travel permission.
# The manager alone may invalidate its onsite state; Automatic still gets the old
# single guarded remote-detail recovery.
Match-Required $alterPlan 'class AlteringCoordinatorFacilityMismatchException' 'remote detail conflict carries a typed manager report'
Match-Required $alterPlan 'catch \(Exception conflict\) when' 'manager receives the child detail contradiction'
Match-Required $alterPlan '_facilityState\.InvalidateOnsite' 'manager invalidates stale onsite authority on detail conflict'
Match-Required (Read-Source 'source/FishingAutomation/AlteringRemoteProcessGuard.cs') 'MustReportToCoordinator' 'managed remote-detail OCR conflict has a dedicated policy'
Match-Required $alter 'MustReportToCoordinator' 'managed queue branches before any remote travel recovery'
Match-Required $alter 'throw new AlteringCoordinatorFacilityMismatchException' 'managed detail contradiction fails closed'
Match-Required $alter '가공 클릭/설비 이동 0회' 'OCR conflict prevents free or paid input and extra travel'
Match-Required $alterTests 'H3: coordinator receives two-frame detail contradiction' 'H3 manager state invalidation regression stays executable'
Match-Required $alterPlan 'AlteringCoordinatorFacilityMismatchException\.IsForFacility' 'manager detects typed conflict even inside safety aggregate'
Match-Required $alterTests 'H3: wing-safety aggregate preserves both failures' 'aggregated currency-verification failure cannot hide onsite invalidation'

# H4: intermediate scheduler must keep the shared manager onsite after a
# confirmed whole-facility receipt. Observation/empty verification comes first.
$dependencyRun = Method-Block $multiAlterDependency 'public async Task RunAsync' '\r?\n    private async Task<AlteringWork\[\]> WaitForFacilityBatchBoundaryAsync' 'intermediate dependency run'
Match-Required $dependencyRun 'CollectReadyBatchAsync\(requestedPlan, ct\)' 'boundary receipt runs through the protected collector'
Match-Required $dependencyRun '_laneState\?\.Observe' 'boundary receipt updates the shared seven-slot ledger'
Match-Required $dependencyRun 'afterFacilityCollection.Length > 0' 'boundary receipt must prove facility empty before granting onsite state'
Match-Required $dependencyRun '_laneState\?\.ConfirmOnsite' 'only the manager confirms same-facility receipt return'
Require (
    $dependencyRun.IndexOf('_laneState?.Observe(') -ge 0 -and
    $dependencyRun.IndexOf('if (afterFacilityCollection.Length > 0)') -gt $dependencyRun.IndexOf('_laneState?.Observe(') -and
    $dependencyRun.IndexOf('_laneState?.ConfirmOnsite(') -gt $dependencyRun.IndexOf('if (afterFacilityCollection.Length > 0)')
) 'H4 boundary onsite confirmation follows ledger observation and verified empty queue'
Match-Required $alterTests 'H4: dependency boundary receipt confirms only the proven same-facility onsite state' 'verified boundary receipt preserves onsite in regression'
Match-Required $alterTests 'H4: first intermediate registration after boundary receipt reuses manager-confirmed facility' 'first boundary handoff queue is a reuse, never an unnecessary travel'
Match-Required $alterTests 'H4: residual facility work after boundary receipt prevents onsite confirmation' 'unfinished or foreign works prevent premature onsite proof'


# 2) Facility ownership remains conservative across nested/intermediate batches.
foreach ($required in @('AssertAccess','AcquireIntermediate','ReleaseIntermediate','IntermediateOwners','IntermediateDepth','ExpectedGrowth')) {
    Match-Required $lane ([regex]::Escape($required)) "facility ownership keeps $required"
}
Match-Required $lane '외부 수령 또는 취소 가능성' 'unexpected facility shrink remains a hard stop'
Match-Required $lane '자동화가 등록하지 않은 작업 증가' 'unexpected facility growth remains a hard stop'
Match-Forbidden $lane 'TapFresh|ClickFresh|DragFresh|SendInput|InterceptionInput|0x39' 'facility ownership ledger remains read-only'

# 3) 13:14 real-world regression: target quantity cannot hand off while a real Stop UI remains.
Match-Required $stopPolicy '!isGathering.*!isAutoTraveling.*!isFishing.*!stopButtonVisible' 'life-skill stop requires idle CLI plus no visible Stop UI'
Match-Forbidden $stopPolicy 'MainButtonState' 'life-skill post-Space policy does not trust stale MainButtonState'
Match-Required $gathering 'IGatheringStopVisualProbe' 'next material start supports a read-only visual stop probe'
Match-Required $gathering 'await probe\.IsStopButtonVisibleAsync\(ct\)' 'stale CLI Stop is visually checked before the next material'

$targetStop = Method-Block $bulk 'private async Task<bool> WaitForLifeSkillHundredStopOrTargetAsync' '\r?\n    private static bool IsOwnedLifeSkillActivity' 'target stop flow'
Match-Required $targetStop '목표 재료 확보' 'target-stop path still triggers as soon as material goal is met'
Match-Required $targetStop 'await StopAsync\(ct\);\s*await ConfirmLifeSkillStoppedAsync' 'target-stop always attempts guarded stop before completion confirmation'

$confirmStop = Method-Block $bulk 'private async Task ConfirmLifeSkillStoppedAsync' '\r?\n    private async Task StartInventoryHundredQuestAsync' 'post-Space stop confirmation'
Match-Required $confirmStop 'IsStopButtonVisibleAsync\(ct\)' 'post-Space confirmation observes the real Stop UI'
Match-Required $confirmStop 'LifeSkillStopPolicy\.IsStoppedAfterSpace' 'post-Space confirmation uses the locked stop policy'
Match-Required $confirmStop 'bool retriedStop = false' 'stop retry remains bounded'
Match-Required $confirmStop '!retriedStop.*stableVisibleStop >= 2' 'second Space requires persistent visible Stop evidence'
Match-Required $confirmStop 'retriedStop = true' 'guarded stop retry can happen at most once'
Match-Required $confirmStop '다음 재료로 넘어가지 않습니다' 'unconfirmed stop blocks the next material'
Match-Required $gatherTests '13:14 regression' '13:14 live failure remains an executable regression case'
Match-Required $gatherTests 'visible Stop UI must not authorize the next material' 'visible Stop handoff rejection remains tested'
Match-Required $gatherTests 'next material starts when CLI Stop lingers' 'stale CLI-only Stop handoff remains tested'

# 3b) 15:47 real-world regression: exact life-skill label OCR can jitter without changing rows.
Match-Required $navPolicy 'IsSameLifeSkillRow' 'life-skill final click has a dedicated same-row geometry policy'
Match-Required $navPolicy 'Math\.Abs\(expectedCenterY - freshCenterY\) <= 24' 'life-skill same-row policy uses bounded vertical tolerance'
$lifeSkillStart = Method-Block $bulk 'private async Task StartLifeSkillHundredAsync' '\r?\n    private async Task<DetectionResult\?> FindStableLifeSkillRowAsync' 'life-skill start flow'
Match-Required $lifeSkillStart 'for \(int pass = 1; pass <= 3; pass\+\+\)' 'life-skill final OCR confirmation has a bounded three-frame retry'
Match-Required $lifeSkillStart 'GatheringNavigationPolicy\.IsSameLifeSkillRow' 'life-skill final click uses row-Y identity instead of rectangle overlap'
Match-Required $lifeSkillStart 'HasRowIconVisual' 'life-skill final click still requires the left row icon'
Match-Required $lifeSkillStart 'exact OCR \+ 같은 행 Y±24 \+ 왼쪽 아이콘' 'life-skill final click logs all three safety signals'
Match-Forbidden $lifeSkillStart 'Bounds\.IntersectsWith\(row\.Value\.Bounds\)' 'life-skill final click no longer requires OCR rectangles to overlap'
Match-Required $gatherTests '15:47 life-skill row regression' '15:47 OCR-box jitter remains an executable regression case'

# 4) 14:23 real-world regression: completion may return to field, but recovery is navigation-only.
$receiptReturn = Method-Block $alter 'private async Task<bool> WaitForReceiptFacilityReturnAsync' '\r?\n    private async Task<bool> CloseCompletionResultAndWaitForFacilityAsync' 'receipt field-return recovery'
Match-Required $receiptReturn '일반 필드 3회 확인' 'field-return recovery needs three stable observations'
Match-Required $receiptReturn 'CanRecoverFieldAfterCompletion' 'field-return recovery uses explicit safety policy'
Match-Required $receiptReturn 'await EnterFacilityAsync\(plan, ct\)' 'field-return recovery performs one bounded facility re-entry'
Match-Required $receiptReturn 'IsProvenReceiptAfterReopen' 'reopened facility requires actual same-recipe queue decrease'
Match-Required $receiptReturn '재수령 Space 금지' 'field-return path explicitly forbids a second receipt Space'
Match-Required $receiptReturn '추가 Space 0회' 'successful recovery records zero additional Space inputs'
Match-Forbidden $receiptReturn 'TapFresh\(0x39|QueueAsync\(|CollectAsync\(|CollectAfterTravelAsync\(' 'field-return recovery cannot send Space, queue, or collect again'
Match-Required $receiptPolicy 'autoTraveling == false' 'field-return policy requires proven non-travel state'
Match-Required $receiptPolicy 'receiptWorkCountBefore is > 0' 'field-return policy requires a known pre-receipt baseline'
Match-Required $receiptPolicy 'IsCliReceiptConfirmed\(start, end\)' 'post-reopen success remains queue-decrease authoritative'
Match-Required $alterTests '14:23 receipt regression' '14:23 live failure remains an executable regression case'
Match-Required $alterTests 'unknown CLI state blocks field re-entry' 'unknown CLI state cannot authorize field recovery'
Match-Required $alterTests 'reopened facility cannot falsely confirm unchanged' 'unchanged queue cannot falsely confirm receipt'

# 4b) 16:43 real-world regression: transient move-button disappearance cannot prove arrival.
$facilityTravel = Method-Block $alter 'private async Task TravelToFacilityAsync' '\r?\n    // Free navigation only' 'facility travel arrival'
Match-Required $travelPolicy 'RequiredOnsiteStableFrames = 7' 'facility arrival requires seven stable observations'
Match-Required $travelPolicy 'RequiredOnsiteStableDuration = TimeSpan\.FromSeconds\(3\)' 'facility arrival requires a three-second stable window'
Match-Required $travelPolicy 'FinalOnsiteRecheckDelay = TimeSpan\.FromMilliseconds\(1200\)' 'facility arrival has a delayed fresh final recheck'
Match-Required $travelPolicy 'autoTraveling == false' 'unknown/stale CLI cannot prove facility arrival'
Match-Required $facilityTravel 'HasStableOnsiteEvidence' 'travel flow uses the long stable-arrival policy'
Match-Required $facilityTravel 'FinalOnsiteRecheckDelay' 'travel flow performs the delayed final recheck'
Match-Required $facilityTravel '설비 도착 후보 후행 재확인 실패' 'failed late arrival verification returns to waiting'
Match-Required $facilityTravel 'CLI AutoTraveling=false' 'successful arrival records proven non-travel CLI'
Match-Required $alterTests '16:43 arrival regression' '16:43 transient move-button disappearance remains an executable regression case'

# 4c) 17:21 real-world regression: top-right currency/X ambiguity cannot hide a real move button.
Match-Required $alter 'MoveButtonAnchorArea' 'move-button detector uses the fixed left-side anchor area'
Match-Required $alter 'anchorTeal \* 100 >= anchorSampled \* 18' 'move-button anchor requires substantial teal fill'
Match-Required $alter 'moveAnchorVisible: moveAnchor' 'move-button policy receives independent fixed-anchor proof'
Match-Required $alterTests '17:21 regression' '17:21 currency/X veto failure remains an executable regression case'
Match-Required $alterTests 'isolated anchor color cannot masquerade' 'remote move detection still requires broad shape plus anchor'

# 4d) 20:19 live regression: a fresh facility entry must move before any recipe selection.
$queueFlow = Method-Block $alter 'public async Task QueueAsync' '\r?\n    private async Task RecoverRemoteDetailToOnsiteAsync' 'queue facility-first order'
Match-Required $queueFlow 'TryReuseOnsiteFacilityAsync' 'only proven same-facility reuse may skip a fresh move'
Match-Required $queueFlow 'TravelToFacilityAsync\(plan, ct, forceMoveClick: true\)' 'fresh facility entry forces one move before recipe selection'
Match-Required $queueFlow '새 시설 첫 등록' 'fresh facility move-first order is explicit in diagnostics'
Match-Required $queueFlow 'await SelectRecipeAsync\(plan, directive, ct\)' 'recipe selection remains after facility travel with manager directive'
Match-Required $facilityTravel '설비 이동 1회 필수 경로' 'forced first-entry travel ignores move-detector false negatives for ordering'
Match-Required $facilityTravel '버튼 검출 결과로 품목 선택 순서를 바꾸지 않음' 'move detector cannot authorize recipe-before-move'
Match-Required $facilityTravel '품목 선택 전 실행' 'one-shot move input is explicitly before recipe selection'
Match-Forbidden $facilityTravel '최초 진입 이동버튼 미검출' 'fresh entry can no longer downgrade into onsite proof before moving'

# 4e) 19:21 root cause: the move click succeeds, but the optional center-screen travel dialog must be confirmed.
Match-Forbidden $alter 'ClickFacilityMoveWithRetryAsync' 'facility move is never re-clicked after a successful one-shot input'
Match-Forbidden $travelPolicy 'MaxMoveClickAttempts|InitialMoveClickSettleDelay|MoveReactionProbeDelay' 'misdiagnosed click-retry timing logic stays removed'
Match-Required $alter 'FacilityTravelDialog = new\(80, 260, 640, 700\)' 'travel wording OCR covers the center-screen popup'
Match-Required $alter 'FacilityTravelConfirmVisual = new\(120, 340, 560, 620\)' 'travel confirmation visual covers the center/lower modal area'
Match-Required $alter 'HasFacilityTravelConfirmationVisual' 'travel flow has a dedicated center-popup visual detector'
Match-Required $travelPolicy 'MaxTravelConfirmationSpaces = 2' 'same proven travel popup has at most one Space retry'
Match-Required $facilityTravel 'ShouldConfirmAfterMoveClick' 'whole travel wait loop continuously watches the optional travel popup'
Match-Required $facilityTravel '입력 1회 고정 · 재클릭 금지' 'facility move click is one-shot'
Match-Required $facilityTravel '이동 확인창 감지' 'center travel popup is confirmed with Space'
Match-Required $facilityTravel '이동 확인창 닫힘 확인' 'travel popup close is verified before continuing'
Match-Required $facilityTravel '추가 설비 이동 클릭 없이 정지' 'failed transition never re-clicks the facility move control'
Match-Required $alterTests '19:21 regression' '19:21 missed center-popup root cause remains executable regression coverage'

# 5) Gathering handoff to processing keeps the proven no-Space UI unwind.
$fieldExit = Method-Block $alter 'public async Task ExitToFieldAsync' '\r?\n    public async Task RecoverStallAsync' 'processing-to-field exit'
Match-Required $fieldExit '일반 필드 2프레임 확인' 'processing UI exit still requires stable field confirmation'
Match-Required $fieldExit 'TapFresh\(0x01, ct\)' 'processing UI exit unwinds with Escape'
Match-Forbidden $fieldExit 'TapFresh\(0x39' 'processing UI exit never presses Space'

Write-Host "PASS $checks stable production behavior locks"
