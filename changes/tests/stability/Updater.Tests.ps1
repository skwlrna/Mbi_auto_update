param([Parameter(Mandatory=$true)][string]$Updater)
$ErrorActionPreference='Stop'
if(-not $env:RUNNER_TEMP){$env:RUNNER_TEMP=[IO.Path]::GetTempPath()}
$testRoot=Join-Path $env:RUNNER_TEMP ('updater-test-' + [Guid]::NewGuid().ToString('N'))
New-Item $testRoot -ItemType Directory | Out-Null
function global:Start-Sleep { param($Seconds,$Milliseconds) }
function global:Get-Process { param($Id,$Name,$ErrorAction) return $null }
function global:Add-Type { param($AssemblyName) }
function global:Start-Process {
    param($FilePath,$WorkingDirectory)
    if ($global:healthyTest -and (Test-Path (Join-Path $global:installTest '.update_pending'))) {
        Copy-Item (Join-Path $global:installTest '.update_pending') (Join-Path $global:installTest '.update_healthy') -Force
    }
}
function global:Copy-Item {
    param($Path,$Destination,$LiteralPath,[switch]$Recurse,[switch]$Force,$ErrorAction)
    $src=if($LiteralPath){$LiteralPath}else{$Path}
    if ($global:failCopyTest -and $Destination -eq $global:failCopyTest) {
        $global:failCopyTest=$null
        throw 'Injected copy failure'
    }
    # Preserve the production caller's error policy for optional settings files.
    $copyErrorAction = if ($ErrorAction) { $ErrorAction } else { 'Stop' }
    Microsoft.PowerShell.Management\Copy-Item -Path $src -Destination $Destination -Recurse:$Recurse -Force:$Force -ErrorAction $copyErrorAction
}
try {
    foreach($case in @('success','copy-failure','health-failure','backup-failure')) {
        $global:installTest=Join-Path $testRoot $case
        $incoming=Join-Path $testRoot ($case+'-incoming')
        foreach($base in @($global:installTest,$incoming)) {
            New-Item (Join-Path $base 'release'),(Join-Path $base 'tools'),(Join-Path $base 'FishingAutomation') -ItemType Directory -Force | Out-Null
            'fake start' | Set-Content (Join-Path $base 'START.cmd')
            'fake updater' | Set-Content (Join-Path $base 'tools/ApplyUpdate.ps1')
        }
        'old exe' | Set-Content (Join-Path $global:installTest 'release/FishingAutomation.exe')
        'new exe' | Set-Content (Join-Path $incoming 'release/FishingAutomation.exe')
        '{"private":"keep"}' | Set-Content (Join-Path $global:installTest 'release/notification.json')
        # A REAL F9 resume record lives under user LocalAppData, outside
        # InstallRoot. The update must not touch it even on a failed install,
        # rollback or restart. Keep the bytes and batch identity unchanged.
        $localAppData = Join-Path $testRoot ($case+'-LocalAppData')
        $batchId = [Guid]::NewGuid().ToString('N')
        $f9Dir = Join-Path $localAppData ("MabiAuto/multi-altering/fresh-runs/f9-"+$batchId)
        New-Item -ItemType Directory -Force -Path $f9Dir | Out-Null
        $manifest = Join-Path $f9Dir 'batch.json'
        ('{"Version":2,"BatchId":"'+$batchId+'","State":0,"Items":[{"TargetQuantity":100,"QueuedWorks":3,"ConfirmedReceivedWorks":2}]}') |
            Set-Content -LiteralPath $manifest -Encoding UTF8
        $before = [IO.File]::ReadAllBytes($manifest)
        $zip=Join-Path $testRoot ($case+'.zip')
        Compress-Archive (Join-Path $incoming '*') $zip
        $global:healthyTest=$case -ne 'health-failure'
        $global:failCopyTest=if($case -eq 'copy-failure'){Join-Path $global:installTest 'release'}elseif($case -eq 'backup-failure'){Join-Path $global:installTest '.update_rollback/release'}else{$null}
        & $Updater -ZipPath $zip -InstallRoot $global:installTest -ProcessId 987654
        $actual=(Get-Content (Join-Path $global:installTest 'release/FishingAutomation.exe') -Raw).Trim()
        $expected=if($case -eq 'success'){'new exe'}else{'old exe'}
        if($actual -ne $expected){throw "$case failed: $actual != $expected"}
        $settings=Get-Content (Join-Path $global:installTest 'release/notification.json') -Raw
        if(-not $settings.Contains('keep')){throw "$case lost user settings"}
        if(Test-Path (Join-Path $global:installTest '.update_pending')){throw "$case left pending health marker"}
        $after = [IO.File]::ReadAllBytes($manifest)
        if(-not ([BitConverter]::ToString($before) -eq [BitConverter]::ToString($after))) {
            throw "$case altered durable F9 resume manifest"
        }
        $readBack = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
        if($readBack.BatchId -ne $batchId -or $readBack.Items[0].TargetQuantity -ne 100 -or
            $readBack.Items[0].QueuedWorks -ne 3 -or $readBack.Items[0].ConfirmedReceivedWorks -ne 2) {
            throw "$case lost F9 goal/ledger after update or rollback"
        }
        if((@(Get-ChildItem -LiteralPath (Split-Path $f9Dir -Parent) -Directory)).Count -ne 1) {
            throw "$case duplicated user F9 batch after update"
        }
        Write-Host "PASS updater $case preserves settings, F9 resume batch and correct executable"
    }
} finally {
    foreach($name in @('Start-Sleep','Get-Process','Add-Type','Start-Process','Copy-Item')) { Remove-Item "Function:\global:$name" -ErrorAction SilentlyContinue }
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
