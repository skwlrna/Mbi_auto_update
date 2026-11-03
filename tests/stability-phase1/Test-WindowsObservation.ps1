#requires -Version 5.1
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '..\..\tools\validation\Invoke-MacroStabilityObservation.ps1'
$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("mabi-stability-smoke-" + [Guid]::NewGuid().ToString("N"))
[void][System.IO.Directory]::CreateDirectory($tempDir)

try {
    $heartbeat = Join-Path $tempDir ("watchdog_heartbeat_{0}.txt" -f $PID)
    $events = Join-Path $tempDir 'events.jsonl'
    [System.IO.File]::WriteAllText($events, '')
    $okPath = Join-Path $tempDir 'clean.json'
    $baseArgs = @{
        ProcessId = $PID
        ApplicationDir = $tempDir
        EventsPath = $events
        SampleSeconds = 1
    }

    # The test observes the current PowerShell process, NOT the game.
    [System.IO.File]::WriteAllText($heartbeat, [DateTime]::UtcNow.Ticks.ToString())
    & $source @baseArgs -ReportPath $okPath -Samples 2 -FailOnAnomaly

    if (-not (Test-Path -LiteralPath $okPath)) { throw 'Clean report missing' }
    $ok = Get-Content -LiteralPath $okPath -Raw | ConvertFrom-Json
    if ($ok.assessment -ne 'OBSERVATION_ONLY' -or
        $ok.samplesObserved -ne 2 -or
        -not $ok.watchdogHeartbeatObserved -or
        $ok.inputCommandsSent -ne 0 -or
        $ok.gameTasksVerified -ne $false -or
        $ok.updateOrReleaseTriggered -ne $false) {
        throw 'Clean read-only observation contract violated'
    }

    $stalePath = Join-Path $tempDir 'stale.json'
    [System.IO.File]::WriteAllText($heartbeat, [DateTime]::UtcNow.AddMinutes(-3).Ticks.ToString())
    & $source @baseArgs -ReportPath $stalePath -Samples 1
    $stale = Get-Content -LiteralPath $stalePath -Raw | ConvertFrom-Json
    if ($stale.assessment -ne 'REVIEW_REQUIRED' -or
        @($stale.warnings) -notcontains 'HEARTBEAT_STALE_90S' -or
        $stale.inputCommandsSent -ne 0) {
        throw 'Stale heartbeat review policy violated'
    }

    $missingPath = Join-Path $tempDir 'missing.json'
    Remove-Item -LiteralPath $heartbeat -Force
    & $source @baseArgs -ReportPath $missingPath -Samples 1
    $missing = Get-Content -LiteralPath $missingPath -Raw | ConvertFrom-Json
    if ($missing.assessment -ne 'REVIEW_REQUIRED' -or
        @($missing.warnings) -notcontains 'HEARTBEAT_MISSING') {
        throw 'Missing heartbeat review policy violated'
    }

    Write-Host 'WINDOWS_READ_ONLY_OBSERVATION_SMOKE_OK (3 cases; actual game input: 0)'
}
finally {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}
