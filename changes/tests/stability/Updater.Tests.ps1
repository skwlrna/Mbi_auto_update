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
        Write-Host "PASS updater $case preserves settings and correct executable"
    }
} finally {
    foreach($name in @('Start-Sleep','Get-Process','Add-Type','Start-Process','Copy-Item')) { Remove-Item "Function:\global:$name" -ErrorAction SilentlyContinue }
    Remove-Item $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
