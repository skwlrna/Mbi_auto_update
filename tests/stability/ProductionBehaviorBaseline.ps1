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

# 1) Coordinators schedule verified engines only. They never acquire direct-input authority.
Match-Required $multiGather 'new GatheringAutomation\(' 'multi-gather delegates every material to GatheringAutomation'
Match-Required $multiGather '시작 직전 재고' 'multi-gather rechecks inventory immediately before each material'
Match-Forbidden $multiGather 'TapFresh|ClickFresh|DragFresh|SendInput|InterceptionInput|0x39' 'multi-gather owns no direct input or Space'
Match-Required $multiAlter 'facilityWorks\.All\(x => x\.IsCompleted\)' 'multi-altering waits for whole facility batch completion'
Match-Required $multiAlter 'runBatch\(plan, 1, ct\)' 'same-facility work remains one-slot round-robin'
Match-Required $multiAlter '같은 시설 여러 품목은 라운드로빈 혼합' 'round-robin mixed-facility behavior remains explicit'
Match-Required $multiAlter '배치 전체 완료 전 이동 없음' 'partial slot completion never causes a facility move'
Match-Forbidden $multiAlter 'TapFresh|ClickFresh|DragFresh|SendInput|InterceptionInput|ProductionUiRuntime|0x39' 'multi-altering coordinator owns no direct UI input'

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

# 5) Gathering handoff to processing keeps the proven no-Space UI unwind.
$fieldExit = Method-Block $alter 'public async Task ExitToFieldAsync' '\r?\n    public async Task RecoverStallAsync' 'processing-to-field exit'
Match-Required $fieldExit '일반 필드 2프레임 확인' 'processing UI exit still requires stable field confirmation'
Match-Required $fieldExit 'TapFresh\(0x01, ct\)' 'processing UI exit unwinds with Escape'
Match-Forbidden $fieldExit 'TapFresh\(0x39' 'processing UI exit never presses Space'

Write-Host "PASS $checks stable production behavior locks"
