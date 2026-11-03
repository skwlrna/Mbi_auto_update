#requires -Version 5.1
<#
.SYNOPSIS
Read-only local monitoring of an already-running MabiAuto process.
.DESCRIPTION
Never opens the game, clicks, sends keys, restarts an application, modifies
its state, records screenshots, reads inventory or uploads any data.
The result is observational ONLY; it cannot prove in-game crafting or Abyss completion.
.EXAMPLE
.\tools\validation\Invoke-MacroStabilityObservation.ps1 -ProcessId 12345 -DurationMinutes 120
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 2147483647)]
    [int]$ProcessId,

    [string]$ApplicationDir = '',

    [ValidateRange(1, 3600)]
    [int]$SampleSeconds = 10,

    [ValidateRange(1, 1440)]
    [int]$DurationMinutes = 120,

    # 0 = derive sample count from duration. For CI use 2.
    [ValidateRange(0, 100000)]
    [int]$Samples = 0,

    [string]$EventsPath = '',

    [string]$ReportPath = '',

    [switch]$FailOnAnomaly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-UniqueCode {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Code
    )
    if (-not $List.Contains($Code)) { [void]$List.Add($Code) }
}

$started = [DateTimeOffset]::UtcNow
$proc = Get-Process -Id $ProcessId -ErrorAction Stop
$originalStartUtc = $proc.StartTime.ToUniversalTime()

if ([string]::IsNullOrWhiteSpace($ApplicationDir)) {
    if ([string]::IsNullOrWhiteSpace($proc.Path)) {
        throw '프로세스 실행 파일 경로를 읽을 수 없습니다. -ApplicationDir 경로를 지정하세요.'
    }
    $ApplicationDir = Split-Path -Parent $proc.Path
}
if (-not (Test-Path -LiteralPath $ApplicationDir -PathType Container)) {
    throw 'ApplicationDir 경로를 찾지 못했습니다.'
}

if ([string]::IsNullOrWhiteSpace($EventsPath)) {
    $EventsPath = Join-Path $env:LOCALAPPDATA 'MabiAuto\macro-stability-events.jsonl'
}
if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $reportDir = Join-Path $env:LOCALAPPDATA 'MabiAuto\validation'
    $ReportPath = Join-Path $reportDir ('observation-{0}-{1}.json' -f $ProcessId, $started.ToString('yyyyMMdd-HHmmss'))
}
$targetDir = Split-Path -Parent $ReportPath
if ([string]::IsNullOrWhiteSpace($targetDir)) {
    throw 'ReportPath는 디렉터리를 포함한 전체 경로여야 합니다.'
}
$heartbeat = Join-Path $ApplicationDir ('watchdog_heartbeat_{0}.txt' -f $ProcessId)
$warnings = [System.Collections.Generic.List[string]]::new()
$workingSet = [System.Collections.Generic.List[double]]::new()
$heartbeatAges = [System.Collections.Generic.List[double]]::new()
$sampleCount = if ($Samples -gt 0) { $Samples } else {
    [int][Math]::Ceiling($DurationMinutes * 60.0 / $SampleSeconds) + 1
}
$observed = 0
$processAlive = $true

Write-Host ('관찰 시작: PID={0}, 간격={1}s, 횟수={2}; 게임 입력 없음' -f $ProcessId, $SampleSeconds, $sampleCount)

for ($index = 0; $index -lt $sampleCount; $index++) {
    if ($index -gt 0) { Start-Sleep -Seconds $SampleSeconds }

    try {
        $proc = Get-Process -Id $ProcessId -ErrorAction Stop
        if ($proc.StartTime.ToUniversalTime() -ne $originalStartUtc) {
            Add-UniqueCode $warnings 'PID_REUSED'
            $processAlive = $false
            break
        }
    }
    catch {
        Add-UniqueCode $warnings 'PROCESS_EXITED'
        $processAlive = $false
        break
    }

    [void]$workingSet.Add([Math]::Round($proc.WorkingSet64 / 1MB, 2))
    $observed++

    if (-not (Test-Path -LiteralPath $heartbeat -PathType Leaf)) {
        Add-UniqueCode $warnings 'HEARTBEAT_MISSING'
        continue
    }

    try {
        $raw = [System.IO.File]::ReadAllText($heartbeat).Trim()
        [long]$ticks = 0
        if (-not [long]::TryParse($raw, [ref]$ticks)) {
            Add-UniqueCode $warnings 'HEARTBEAT_INVALID'
            continue
        }
        $heartbeatUtc = [DateTime]::new($ticks, [DateTimeKind]::Utc)
        $age = ([DateTime]::UtcNow - $heartbeatUtc).TotalSeconds
        if ($age -lt -10) {
            Add-UniqueCode $warnings 'HEARTBEAT_IN_FUTURE'
        }
        elseif ($age -ge 90) {
            Add-UniqueCode $warnings 'HEARTBEAT_STALE_90S'
        }
        [void]$heartbeatAges.Add([Math]::Round($age, 1))
    }
    catch {
        Add-UniqueCode $warnings 'HEARTBEAT_UNREADABLE'
    }

    if (($index + 1) % [Math]::Max(1, [int][Math]::Ceiling(60.0 / $SampleSeconds)) -eq 0) {
        Write-Host ('진행 확인: {0}회 측정; 메모리 {1}MiB; 게임 작업 성공 여부는 미판정' -f $observed, $workingSet[$workingSet.Count - 1])
    }
}

$eventCounts = @{}
$eventsInWindow = 0
$malformedEventCount = 0
$reviewCodes = @('OWNER_CONFLICT_OBSERVED','QUIET_RECOVERY_LIMIT','QUIET_ACTIVITY_WARNING','START_REJECTED')
$eventLogPresent = Test-Path -LiteralPath $EventsPath -PathType Leaf
if (-not $eventLogPresent) { Add-UniqueCode $warnings 'STABILITY_EVENT_LOG_MISSING' }
if ($eventLogPresent) {
    try {
        # The production log rotates at ~1MiB. No raw lines go into the report.
        Get-Content -LiteralPath $EventsPath -Tail 4096 -ErrorAction Stop | ForEach-Object {
            try {
                $record = $_ | ConvertFrom-Json -ErrorAction Stop
                if ($null -ne $record.Utc -and $null -ne $record.Code) {
                    $when = [DateTimeOffset]::Parse([string]$record.Utc)
                    if ($when -ge $started) {
                        $code = [string]$record.Code
                        if ($code -match '^[A-Z0-9_]{1,80}$') {
                            if (-not $eventCounts.ContainsKey($code)) { $eventCounts[$code] = 0 }
                            $eventCounts[$code]++
                            $eventsInWindow++
                        }
                    }
                }
            }
            catch { $malformedEventCount++ }
        }
    }
    catch { Add-UniqueCode $warnings 'EVENT_LOG_UNREADABLE' }
}
foreach ($code in $reviewCodes) {
    if ($eventCounts.ContainsKey($code) -and $eventCounts[$code] -gt 0) {
        Add-UniqueCode $warnings ('REVIEW_EVENT_' + $code)
    }
}

$ended = [DateTimeOffset]::UtcNow
$memoryMin = if ($workingSet.Count -gt 0) { ($workingSet | Measure-Object -Minimum).Minimum } else { $null }
$memoryMax = if ($workingSet.Count -gt 0) { ($workingSet | Measure-Object -Maximum).Maximum } else { $null }
$memoryFirst = if ($workingSet.Count -gt 0) { $workingSet[0] } else { $null }
$memoryLast = if ($workingSet.Count -gt 0) { $workingSet[$workingSet.Count - 1] } else { $null }
$maxHeartbeatAge = if ($heartbeatAges.Count -gt 0) {
    ($heartbeatAges | Measure-Object -Maximum).Maximum
} else { $null }

# The report deliberately excludes process path, account identity, recipe,
# character, packet data, screenshots, credentials and raw log contents.
$report = [ordered]@{
    schemaVersion = 1
    observer = 'read-only-windows'
    startedUtc = $started.ToString('o')
    endedUtc = $ended.ToString('o')
    durationSeconds = [Math]::Round(($ended - $started).TotalSeconds, 1)
    processId = $ProcessId
    processStartUtc = $originalStartUtc.ToString('o')
    processAliveAtEnd = $processAlive
    sampleSeconds = $SampleSeconds
    samplesObserved = $observed
    watchdogHeartbeatObserved = ($heartbeatAges.Count -gt 0)
    maxHeartbeatAgeSeconds = $maxHeartbeatAge
    workingSetMiB = [ordered]@{
        first = $memoryFirst
        last = $memoryLast
        min = $memoryMin
        max = $memoryMax
    }
    stabilityEventLogPresent = $eventLogPresent
    stabilityEventCounts = $eventCounts
    stabilityEventsWithinWindow = $eventsInWindow
    malformedEventLinesIgnored = $malformedEventCount
    warnings = @($warnings.ToArray())
    assessment = if ($warnings.Count -gt 0) { 'REVIEW_REQUIRED' } else { 'OBSERVATION_ONLY' }
    gameTasksVerified = $false
    inputCommandsSent = 0
    updateOrReleaseTriggered = $false
}

[void][System.IO.Directory]::CreateDirectory($targetDir)
$json = $report | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText($ReportPath, $json, [System.Text.UTF8Encoding]::new($false))
Write-Host ('관찰 종료: {0}; 정상 관찰 횟수={1}; 결과 파일={2}' -f $report.assessment, $observed, $ReportPath)
Write-Host '주의: OBSERVATION_ONLY도 실제 게임 동작 성공의 증거가 아닙니다.'
if ($FailOnAnomaly -and $warnings.Count -gt 0) { exit 3 }
