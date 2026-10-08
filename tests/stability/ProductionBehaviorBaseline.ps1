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
# M2 long wait: coordinator must track each facility's actual CLI progress
# across slot-wise RunBatch returns, not reset the watch with every loop.
Match-Required $multiAlter 'class MultiAlteringWaitWatchdog' 'multi coordinator keeps per-facility idle history'
Match-Required $multiAlter 'new MultiAlteringWaitWatchdog\(_idleThreshold, _now\)' 'whole multi run shares a single watchdog across batches'
Match-Required $multiAlter 'idleWatchdog.Observe\(facility, observed\)' 'every pending facility is observed before new actions'
Match-Required $multiAlter 'lowestRunning < previous.LowestRunningSeconds' 'only genuine countdown progress refreshes idle timer'
Match-Required $multiAlter 'idleWatchdog.ConfirmManagerProgress\(facility\)' 'manager-confirmed registration or completion refreshes progress'
Match-Required $multiAlter '다중가공 시설별 정체 감지' 'persistent idle queue fails closed with diagnostic'
Match-Required $multiAlter '_laneState\?\.InvalidateOnsite' 'stalled facility revokes cached manager onsite proof'
Match-Required $alterTests 'M2 wait: unchanged CLI queue across repeated multi-batch polling stops safely' 'M2 long wait timeout has executable regression'
Match-Required $alterTests 'M2 wait: genuine countdown decreases refresh' 'M2 legitimate long work is not prematurely stopped'
Match-Required $alterTests 'M2 wait: stalled wood lane stops after 60s despite progressing metal lane' 'M2 one healthy facility cannot hide another stalled lane'

# M3: saved jobs/sessions do not attest where the character stands after F9.
# The central manager must distinguish Unknown from both verified onsite and
# confirmed remote rather than trusting an always-visible move label.
Match-Required $lane 'enum FacilityLocationProof' 'startup location has explicit cold-start/current-run/uncertain states'
Match-Required $lane 'ColdStartUnknown' 'initial location is unknown, not automatically remote'
Match-Required $lane 'ConfirmedThisRun' 'only same-run verified onsite can be reused'
Match-Required $lane 'RuntimeUncertain' 'uncertain location after field exit/recovery is explicit'
Match-Required $lane 'class FacilityStartupLocationPolicy' 'startup move decision is centralized'
Match-Required $lane 'proof == FacilityLocationProof.ConfirmedThisRun' 'stale saved facility names never authorize reuse'
Match-Required $lane '_locationProof = FacilityLocationProof.ConfirmedThisRun' 'onsite proof is granted only by manager confirmation'
Match-Required $lane '_locationProof = FacilityLocationProof.RuntimeUncertain' 'runtime exit invalidates current-run onsite proof'
Match-Required $lane '초기 위치 미확정\(원격 확정 아님\)' 'cold start distinguishes unknown from remote in logs'
Match-Required (Read-Source 'source/FishingAutomation/MainForm.MultiAltering.cs') '이전 실행의 현장확정은 복원하지 않음' 'multi runner never restores saved physical location'
Match-Required $alterTests 'M3: F9 resume with existing works cannot inherit a prior-run onsite proof' 'M3 restart with saved works begins Fresh'
Match-Required $alterTests 'M3: observing the same CLI seven-slot ledger is not physical location evidence' 'M3 queue observation alone cannot prove onsite'
Match-Required $alterTests 'M3: current-run onsite verification permits same-facility reuse' 'M3 same-run proof permits proper reuse'
Match-Required $alterTests 'M3: post-start location uncertainty is distinct from cold-start unknown' 'M3 later invalidation never recycles stale onsite'
Match-Required $alterTests 'M3: even a stale same-name location cannot authorize reuse' 'M3 cold start policy rejects persisted location hints'

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

# L2: the compiled production screen, not a FakeWorld, must be covered by
# executable Windows regressions for fixed cards, medicine, cached selection,
# two-frame detail conflict, receive and completion return.
$realScreenProject = Read-Source 'tests/altering-screen-integration/Regression.csproj'
$realScreenTests = Read-Source 'tests/altering-screen-integration/Program.cs'
$ciWorkflow = Read-Source '.github/workflows/ci.yml'
Match-Required $realScreenProject 'ProjectReference Include="../../source/FishingAutomation/FishingAutomation.csproj"' 'L2 harness references real production assembly'
Match-Required $realScreenTests 'Assembly.Load\("FishingAutomation"\)' 'L2 harness loads real app assembly'
Match-Required $realScreenTests 'AsyncStateMachineAttribute' 'L2 harness inspects real async screen state machines'
Match-Required $realScreenTests 'TrySelectFixedRecipeAsync' 'L2 tests actual fixed recipe route'
Match-Required $realScreenTests 'TrySelectMedicineRecipeBySearchAsync' 'L2 tests actual medicine route'
Match-Required $realScreenTests 'WaitForReceiptFacilityReturnAsync' 'L2 tests actual receipt facility return route'
Match-Required $realScreenTests 'HasFacilityMoveButtonVisual' 'L2 tests actual teal move detector'
Match-Required $realScreenTests 'HasCollectButtonVisual' 'L2 tests actual blue receive detector'
Match-Required $realScreenTests 'HasBottomConfirmationModal' 'L2 tests actual completion modal detector'
Match-Required $ciWorkflow 'dotnet run --project tests/altering-screen-integration/Regression.csproj' 'L2 executes in Windows CI, not build-only'
Match-Forbidden $realScreenTests 'new FakeWorld' 'L2 does not rely on fake directive-only screen'
Match-Forbidden $realScreenTests 'TapFresh\(' 'L2 tests must not send game Space input'
Match-Forbidden $realScreenTests 'ClickFresh\(' 'L2 tests must not click game controls'

# L1: local screen cache is an Automatic(single-altering) observation,
# never another multi-altering decision authority.
Match-Required $alterPlan 'class AlteringScreenOnsiteCachePolicy' 'L1 local cache ownership policy'
Match-Required $alterPlan 'directive == AlteringFacilityEntryDirective.Automatic \? facilityName : null' 'L1 managed entry cache remains empty'
Match-Required $alterPlan 'managedReceipt \? null : facilityName' 'L1 managed receipt return cache remains empty'
Match-Required $alter 'AlteringScreenOnsiteCachePolicy.MayTrustForReceipt' 'L1 receive cache read is gated by directive'
Match-Required $alter 'AlteringScreenOnsiteCachePolicy.AfterVerifiedFacilityEntry' 'L1 manager entry cannot grant local authority'
Match-Required $alter 'AlteringScreenOnsiteCachePolicy.AfterVerifiedReceiptReturn' 'L1 manager receipt cannot grant local authority'
Match-Required $alterTests 'L1: managed receipt ignores lower screen cache' 'L1 cache cannot affect manager receipt'
Match-Required $alterTests 'L1: successful managed reuse/fresh screen entries' 'L1 cache cannot affect manager entry'
Match-Required $alterTests 'L1: managed receipt return reports success' 'L1 receipt return cache remains single only'

# M5: a CLI receipt count drop is not physical bench evidence. The
# coordinator must remain uncertain until a clean two-frame facility return.
Match-Required $receiptPolicy 'CanConfirmReceiptFacilityReturn' 'M5 receipt return has centralized proof criteria'
Match-Required $receiptPolicy 'autoTraveling == false' 'M5 receipt return needs known idle CLI'
Match-Required $alter 'facilityFrames = cleanReturn \? facilityFrames \+ 1 : 0' 'M5 facility title must stay clean across both frames'
Match-Required $alter 'completionModalVisible: completionModal' 'M5 title behind completion popup cannot prove onsite'
Match-Required $alter 'if \(managedReceipt\)' 'M5 FIELD outcome cannot silently reenter under manager authority'
Match-Required $alter '필드에서 K로 연 가공창은 물리적 현장 증거가 아니므로' 'M5 rejects field K menu as bench proof'
Match-Required $alter '수령 완료창 Space 직전 재검증 실패' 'M5 confirmation Space needs fresh modal proof'
Match-Required $alter 'managedReceipt: directive != AlteringFacilityEntryDirective.Automatic' 'M5 manager receipt flag reaches result handling'
Match-Required $alterPlan '_facilityState\.InvalidateOnsite\([\s\S]*?수령 진입' 'M5 manager clears stale onsite proof before receipt'
Match-Required $alterTests 'M5: facility title behind completion modal must not prove onsite' 'M5 popup overlay regression'
Match-Required $alterTests 'M5: active or unknown CLI movement prevents false onsite reuse' 'M5 movement uncertainty regression'

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

# H5: verified field departure is authoritative evidence that the old
# processing-facility location can no longer be reused. The multi resolver
# reports this to the same manager used by main and intermediate queueing.
Match-Required $multiAlter 'FacilityLaneState' 'facility manager ownership is retained'
Match-Required (Read-Source 'source/FishingAutomation/MainForm.MultiAltering.cs') 'dependencyScheduler: dependencyScheduler,\s*laneState: laneState' 'multi resolver receives the shared lane state'
$resolverH5 = Read-Source 'source/FishingAutomation/RecursiveAlteringSupplyResolver.cs'
Match-Required $resolverH5 'FacilityLaneState\? laneState = null' 'single altering retains optional manager-free resolver'
$exitH5 = Method-Block $resolverH5 'private async Task ExitToFieldAndNotifyManagerAsync' '\r?\n    public async Task ResolveAsync' 'resolver manager field-exit notification'
Match-Required $exitH5 'await fieldExit.ExitToFieldAsync\(ct\)' 'actual field transition must finish before normal onsite invalidation'
Match-Required $exitH5 '실제 필드 복귀 확인' 'confirmed field exit is reported to manager'
Match-Required $exitH5 '필드 이탈 진행 중 위치 미확정' 'failed partial field exit invalidates unknown location'
Match-Required $exitH5 '_laneState\?\.InvalidateOnsite' 'only shared lane manager loses confirmed onsite'
Require ([regex]::Matches($resolverH5,'await ExitToFieldAndNotifyManagerAsync\(').Count -eq 3) 'all three grouped/preflight/fallback field departures notify lane state'
Match-Required $alterTests 'H5: after an intermediate onsite confirmation, a subsequent grouped field exit' 'H5 grouped follow-up gather invalidates onsite'
Match-Required $alterTests 'H5: whole-plan preflight announces confirmed field exit' 'H5 initial preflight invalidates only when leaving'
Match-Required $alterTests 'H5: hidden raw shortage discovered after intermediate processing' 'H5 late recursive gather invalidates onsite'
Match-Required $alterTests 'H5: uncertain or failed field exit invalidates stale onsite' 'H5 failed navigation never reuses stale onsite'
Match-Required $alterTests 'H5: resolver preflight without actual field departure' 'H5 no-departure preserves onsite'

# M1: only proven movement may invalidate onsite; material resolution itself
# is still read-only planning until H5 or intermediate queue reports movement.
$m1Resolve = [regex]::Match(
    $alterPlan,
    'SaveStage\("재료 해결 · "[\s\S]*?await _supplyResolver\.ResolveAsync\(plan, recipe, remainingWorks, ct\);').Value
Require (-not [string]::IsNullOrWhiteSpace($m1Resolve)) 'M1 material resolver call stays in registration flow'
Match-Forbidden $m1Resolve '_facilityState\?\.InvalidateOnsite' 'M1 cannot revoke onsite merely because materials are missing'
Match-Required $m1Resolve '실제 필드/타 시설 이탈 전 현장확정 유지' 'M1 defers location changes to confirmed travel'
Match-Required $alterTests 'M1: material inspection and same-facility resolution without travel retain manager onsite' 'M1 same-site resolution preserves reuse directive'
Match-Required $alterTests 'M1: material inspection cannot invent onsite proof' 'M1 initial unknown requires a first facility move'
Match-Required $alterTests 'M1: material-only failure stops without queueing or revoking proven onsite' 'M1 failed inspection cannot falsely revoke onsite'

# M2: stall UI recovery returns evidence; only the lane manager may restore
# onsite after a previously confirmed location and non-travel UI-only recovery.
Match-Required $alterPlan 'interface IAlteringCoordinatorStallRecoveryScreen' 'M2 coordinator recovery observation interface exists'
Match-Required $alterPlan 'AlteringStallRecoveryPolicy.CanRetainOnsite' 'M2 manager reconciles prior authority with recovery proof'
$m2Manager = Method-Block $alterPlan 'internal async Task RecoverStallUnderManagerAsync' '\r?\n    internal void NoteStage' 'manager stall recovery'
Match-Required $m2Manager '_facilityState.InvalidateOnsite' 'M2 manager invalidates onsite before recovery input'
Match-Required $m2Manager 'RecoverStallForCoordinatorAsync' 'M2 screen only returns observation'
Match-Required $m2Manager '_facilityState.ConfirmOnsite' 'M2 only manager re-confirms valid onsite'
Match-Required $m2Manager '_facilityState.IsOnsiteConfirmed' 'M2 recovery cannot invent a prior confirmed location'
Match-Required $zeroWing 'IAlteringCoordinatorStallRecoveryScreen' 'M2 guarded screen forwards manager recovery'
$m2Screen = Method-Block $alter 'public async Task<AlteringStallRecoveryObservation> RecoverStallForCoordinatorAsync' '\r?\n    public async Task RecoverStallAsync' 'managed stall UI recovery'
Match-Required $m2Screen 'await TryAutoTravelingAsync\(ct\)' 'M2 requires known idle travel state'
Match-Required $m2Screen 'for \(int pass = 0; pass < 2; pass\+\+\)' 'M2 must prove two stable facility frames'
Match-Required $m2Screen 'HasBottomConfirmationModal' 'M2 must reject confirmation popups'
Match-Required $m2Screen 'SameFacilityUiRestoredWithoutTravel' 'M2 reports nontravel UI-only proof'
Match-Forbidden $m2Screen 'HasFacilityMoveButtonVisual\(' 'M2 move button cannot override coordinator location authority'
Match-Forbidden $m2Screen 'TravelToFacilityAsync\(' 'M2 recovery is forbidden from physical facility travel'
Match-Forbidden $m2Screen 'TapFresh\(0x39' 'M2 recovery must never press Space on an ambiguous popup'
Match-Required $alterTests 'M2: coordinator re-confirms previously proven onsite' 'M2 safe same-facility UI-only recovery keeps reuse'
Match-Required $alterTests 'M2: UI recovery cannot create onsite authority' 'M2 unknown previous location remains Fresh'
Match-Required $alterTests 'M2: popup, travel, missing header or unknown CLI' 'M2 ambiguous recovery remains Fresh'
Match-Required $alterTests 'M2: manager invalidates onsite before failed stall recovery' 'M2 failed recovery revokes old proof'
Match-Required $alterTests 'M2: single-altering keeps its bounded legacy recovery' 'M2 single-altering legacy isolation'





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
Match-Required $receiptReturn 'IsProvenReceiptAfterReopen' 'reopened facility requires actual facility-wide queue decrease'
Match-Required $receiptReturn '재수령 Space 금지' 'field-return path explicitly forbids a second receipt Space'
Match-Required $receiptReturn '추가 Space 0회' 'successful recovery records zero additional Space inputs'
Match-Forbidden $receiptReturn 'TapFresh\(0x39|QueueAsync\(|CollectAsync\(|CollectAfterTravelAsync\(' 'field-return recovery cannot send Space, queue, or collect again'
Match-Required $receiptPolicy 'autoTraveling == false' 'field-return policy requires proven non-travel state'
Match-Required $receiptPolicy 'receiptWorkCountBefore is > 0' 'field-return policy requires a known pre-receipt baseline'
Match-Required $receiptPolicy 'IsCliReceiptConfirmed\(start, end\)' 'post-reopen success remains queue-decrease authoritative'
Match-Required $alterTests '14:23 receipt regression' '14:23 live failure remains an executable regression case'
Match-Required $alterTests 'unknown CLI state blocks field re-entry' 'unknown CLI state cannot authorize field recovery'
Match-Required $alterTests 'reopened facility cannot falsely confirm unchanged' 'unchanged queue cannot falsely confirm receipt'

# M4: "모두 받기" is scoped to an entire facility, not the current recipe.
# Mixed-slot receipts are blocked before input unless all jobs are complete;
# the post-click managed proof requires a fully drained facility queue.
Match-Required $receiptPolicy 'CanCollectManagedFacility' 'M4 manager can only receive a fully completed facility batch'
Match-Required $receiptPolicy 'facilityWorks.All\(x => x.IsCompleted\)' 'M4 all seven slots must be done before receive-all'
Match-Required $receiptPolicy 'IsManagedFacilityReceiptConfirmed' 'M4 managed receive requires whole-facility empty queue'
Match-Required $receiptPolicy 'before > 0 && after == 0' 'M4 rejects partial decreases as mixed-batch receipt success'
Match-Required $alterPlan 'CanCollectManagedFacility\(works, plan.FacilityName\)' 'M4 manager checks full facility before any receive input'
Match-Required $alterPlan 'IsManagedFacilityReceiptConfirmed' 'M4 manager checks full facility after receive input'
Match-Required $alter 'receiptWorkCountBefore = await TryFacilityWorkCountAsync\(plan, ct\)' 'M4 screen captures facility-wide pre-Space count'
Match-Forbidden $alter 'TryMatchingWorkCountAsync' 'M4 screen cannot confuse a chosen recipe with the whole receiving facility'
Match-Required $receiptReturn 'TryFacilityWorkCountAsync\(plan, ct\)' 'M4 field-return receipt proof uses facility-wide CLI'
Match-Required $alterTests 'M4: manager verifies blue receive-all removed both selected and other recipe' 'M4 mixed recipe receive-all succeeds only when fully cleared'
Match-Required $alterTests 'M4: partial mixed facility batch is rejected before moving' 'M4 partial batch never authorizes blue receive or movement'
Match-Required $alterTests 'M4: selected item disappearing while another completed recipe remains fails closed' 'M4 partial drain stops without a second receive'


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

# N04 (fresh F9): the manager retains every order and each new run receives
# its own isolated durable ledger. Historical F05 is never discarded or loaded.
$startAlter = Read-Source 'source/FishingAutomation/MainForm.Altering.cs'
$startMulti = Read-Source 'source/FishingAutomation/MainForm.MultiAltering.cs'
$batchStore = Read-Source 'source/FishingAutomation/MultiAlteringBatchStore.cs'
$productionPage = Read-Source 'source/FishingAutomation/MainForm.ProductionPage.cs'
$f9Entry = Method-Block $startAlter 'private async Task StartAlteringAsync' '\r?\n        _starting = true;' 'F9 altering entry'
Match-Required $f9Entry 'await StartMultiAlteringAsync\(orders\)' 'all user F9 altering orders flow through multi supervisor'
Match-Required $startMulti 'new MultiAlteringCoordinator\(' 'supervisor owns the whole new multi batch'
Match-Required $startMulti 'new MultiAlteringBatchStore\(mainSessionDir\)' 'supervisor holds root mutex across every fresh run'
Match-Required $startMulti 'fresh-runs' 'fresh F9 stores durable records in a unique namespace'
Match-Required $startMulti 'OpenFreshAsync' 'F9 uses explicitly fresh-only N02/F05 opening path'
Match-Forbidden $startMulti 'OpenNewLimitedTestAsync|batchStore.OpenAsync' 'F9 never opens past resumable or limited-test manifests'
Match-Required $batchStore 'internal async Task OpenFreshAsync' 'fresh-only store gate exists'
Match-Required $batchStore 'allowPreexistingSelectedFacilityWorks: true' 'F9 accepts already occupied live selected facilities'
Match-Required $batchStore 'InitialExistingWorks' 'unrelated earlier game output cannot satisfy the new target'
Match-Required $batchStore 'allowSingleCharacter: true, allowPreexistingSelectedFacilityWorks: true' 'fresh gate retains full N02/F05 journaling'
Match-Forbidden $productionPage 'ReadPendingPlans\(' 'new F9 UI never automatically selects a historical roster'
Match-Required (Read-Source 'tests/n02-resume/Program.cs') 'K/fresh F9 ignores but preserves old unresolved F05 journal' 'F05 old-record isolation is executable regression'
Match-Required (Read-Source 'tests/n02-resume/Program.cs') 'K/previous F9 one registered then restart orders a NEW full target' 'fresh 100 target excludes old 1 job after restart'
Match-Required (Read-Source 'tests/n02-resume/Program.cs') 'K/existing running work remains untouched while six free slots are registered' 'F9 registers into vacant slots even when older work is running'
Match-Required (Read-Source 'tests/n02-resume/Program.cs') 'K/fresh F9 coordinator fills initial vacancies around old running or completed work' 'manager itself fills existing six vacancies'
Match-Required $startMulti 'fillInitialVacancies: true' 'fresh F9 opts in to preserving older occupied slots'
Match-Required (Read-Source 'source/FishingAutomation/MultiAlteringCoordinator.cs') 'startupVacancies' 'coordinator initial vacancy fill is scoped to first pass'
Match-Forbidden $startMulti 'MultiAlteringFreshStartCleanup|이전 작업 정리 완료' 'F9 does not pre-clean or wait out old works'

# V3.1.59: after the FIRST verified fixed coordinate registration, repeat
# facility and recipe checks must not depend on Korean OCR again. Preserve
# original fixed move geometry and CLI/visual no-blind-input guards.
$fixedAlter = Read-Source 'source/FishingAutomation/dungeon/AlteringScreen.cs'
$fixedOcr = Read-Source 'source/FishingAutomation/dungeon/OcrRecognizer.cs'
Match-Required $fixedAlter '_verifiedFacilityTitles' 'first successful manager facility verification is stored only in this run'
Match-Required $fixedAlter '_verifiedFixedRecipes' 'first successful exact fixed recipe verification is stored only in this run'
Match-Required $fixedAlter '_repeatOcrFreeRecipe' 'known fixed recipe repeat avoids OCR'
Match-Required $fixedAlter 'HasFixedFacilityHeaderVisual' 'facility title and subtitle use fixed pixel anchors'
Match-Required $fixedAlter '_postTravelProvenFacilityTitle' 'after proven first arrival re-entry cannot fail on a second OCR pass'
Match-Required $fixedOcr 'new Rectangle\(12, 24, 250, 80\)' 'first title OCR covers leftmost title glyph'
Match-Required (Read-Source 'tests/altering-screen-integration/Program.cs') 'verified repeat detail uses fixed free-button shape without OCR' 'real screen OCR-free repeated path is tested'
Match-Required $fixedAlter 'RequireManagedIdleAsync\(' 'managed CLI activity safety checks remain in place'
Match-Required $fixedAlter 'TryFindFreeProcessButtonVisual\(' 'repeat still checks the free processing button geometry'

# 5) Gathering handoff to processing keeps the proven no-Space UI unwind.
$fieldExit = Method-Block $alter 'public async Task ExitToFieldAsync' '\r?\n    public async Task RecoverStallAsync' 'processing-to-field exit'
Match-Required $fieldExit '일반 필드 2프레임 확인' 'processing UI exit still requires stable field confirmation'
Match-Required $fieldExit 'TapFresh\(0x01, ct\)' 'processing UI exit unwinds with Escape'
Match-Forbidden $fieldExit 'TapFresh\(0x39' 'processing UI exit never presses Space'

Write-Host "PASS $checks stable production behavior locks"
