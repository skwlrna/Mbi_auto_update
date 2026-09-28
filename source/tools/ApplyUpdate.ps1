param(
    [Parameter(Mandatory=$true)][string]$ZipPath,
    [Parameter(Mandatory=$true)][string]$InstallRoot,
    [Parameter(Mandatory=$true)][int]$ProcessId
)

$ErrorActionPreference = 'Stop'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$work = Join-Path $env:TEMP "MabiAutoUpdate_$stamp"
$preserveRoot = Join-Path $work 'preserve'
$extract = Join-Path $work 'extract'
$rollback = Join-Path $InstallRoot '.update_rollback'
$previous = Join-Path $InstallRoot '.previous_version'
$pending = Join-Path $InstallRoot '.update_pending'
$healthy = Join-Path $InstallRoot '.update_healthy'
$log = Join-Path $InstallRoot 'update.log'
$token = [Guid]::NewGuid().ToString('N')
$backupComplete = $false
$replacementStarted = $false

function Log([string]$text) {
    if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).Length -gt 5MB) {
        Clear-Content -LiteralPath $log
    }
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $text" | Tee-Object -FilePath $log -Append | Out-Null
}

function Start-MabiAuto {
    $startCmd = Join-Path $InstallRoot 'START.cmd'
    if (Test-Path -LiteralPath $startCmd) {
        Log 'Starting MABI AUTO through START.cmd.'
        Start-Process -FilePath $startCmd -WorkingDirectory $InstallRoot
        return
    }

    $exe = Join-Path $InstallRoot 'release\FishingAutomation.exe'
    if (Test-Path -LiteralPath $exe) {
        Log 'START.cmd missing; starting release\FishingAutomation.exe directly.'
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent)
        return
    }

    throw '업데이트 후 실행 파일을 찾지 못했습니다. START.cmd와 release\FishingAutomation.exe가 모두 없습니다.'
}


function Stop-InstallWatchdogs {
    Log 'Stopping MacroWatchdog processes before file replacement.'
    Get-Process -Name 'MacroWatchdog' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $path = $_.Path
            if ($path -and $path.StartsWith($InstallRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                try { $_.WaitForExit(3000) } catch { }
            }
        } catch { }
    }
    for ($i = 0; $i -lt 20; $i++) {
        $left = @(Get-Process -Name 'MacroWatchdog' -ErrorAction SilentlyContinue | Where-Object {
            try { $_.Path -and $_.Path.StartsWith($InstallRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) } catch { $false }
        })
        if ($left.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    }
    Start-Sleep -Milliseconds 400
}

function Copy-TreeItem([string]$src, [string]$dst) {
    if (-not (Test-Path -LiteralPath $src)) { return }
    $item = Get-Item -LiteralPath $src
    if ($item.PSIsContainer) {
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        Copy-Item -Path (Join-Path $src '*') -Destination $dst -Recurse -Force -ErrorAction Stop
    } else {
        New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
    }
}

$replaceNames = @(
    'release','FishingAutomation','tools','START.cmd','1_INSTALL_INTERCEPTION.cmd',
    'SETUP_PHONE_ALERT.cmd','SETUP_PHONE_ALERT_ALT.bat','ADD_TEMPLATES.cmd','OPEN_TEMPLATES.cmd',
    'PHONE_ALERT_README.txt','AUTO_UPDATE_README.txt','WINDOWS_LITE.txt','ROLLBACK_PREVIOUS.cmd'
)

$preserve = @(
    'FishingAutomation\notification.json',
    'FishingAutomation\config.json',
    'FishingAutomation\interception.dll',
    'FishingAutomation\templates',
    'FishingAutomation\abyss\templates',
    'FishingAutomation\dungeon\templates',
    'release\notification.json',
    'release\config.json',
    'release\interception.dll',
    'release\templates',
    'release\abyss\templates',
    'release\dungeon\templates'
)

try {
    New-Item -ItemType Directory -Force -Path $work,$preserveRoot,$extract | Out-Null
    Log "Updater start. zip=$ZipPath"

    for ($i = 0; $i -lt 120; $i++) {
        if (-not (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 500
    }
    if (Get-Process -Id $ProcessId -ErrorAction SilentlyContinue) {
        throw '기존 프로그램이 종료되지 않아 파일 교체를 취소합니다.'
    }
    Stop-InstallWatchdogs

    Expand-Archive -LiteralPath $ZipPath -DestinationPath $extract -Force
    $children = @(Get-ChildItem -LiteralPath $extract -Force)
    $dirs = @($children | Where-Object { $_.PSIsContainer })
    if ($children.Count -eq 1 -and $dirs.Count -eq 1) { $sourceRoot = $dirs[0].FullName } else { $sourceRoot = $extract }

    $incomingExe = Join-Path $sourceRoot 'release\FishingAutomation.exe'
    if (-not (Test-Path -LiteralPath $incomingExe)) {
        throw '업데이트 패키지에 release\FishingAutomation.exe가 없습니다. Windows_Lite 패키지를 사용하세요.'
    }

    $incomingUpdater = Join-Path $sourceRoot 'tools\ApplyUpdate.ps1'
    if (-not (Test-Path -LiteralPath $incomingUpdater)) {
        throw '업데이트 패키지에 tools\ApplyUpdate.ps1가 없습니다. 다음 업데이트가 끊기지 않도록 updater 포함 패키지가 필요합니다.'
    }

    if (Test-Path -LiteralPath $rollback) { Remove-Item -LiteralPath $rollback -Recurse -Force -ErrorAction Stop }
    New-Item -ItemType Directory -Force -Path $rollback | Out-Null
    foreach ($name in $replaceNames) {
        Copy-TreeItem (Join-Path $InstallRoot $name) (Join-Path $rollback $name)
    }
    $backupComplete = $true
    Log 'Rollback backup created.'

    foreach ($rel in $preserve) {
        Copy-TreeItem (Join-Path $InstallRoot $rel) (Join-Path $preserveRoot $rel)
    }

    $replacementStarted = $true
    # V0.1.48: only delete install directories that the incoming package actually replaces.
    # This keeps tools/ApplyUpdate.ps1 and source/config files alive for release-only packages.
    foreach ($name in @('release','FishingAutomation','tools')) {
        $incoming = Join-Path $sourceRoot $name
        if (Test-Path -LiteralPath $incoming) {
            if (Test-Path -LiteralPath (Join-Path $InstallRoot $name)) { Remove-Item -LiteralPath (Join-Path $InstallRoot $name) -Recurse -Force -ErrorAction Stop }
        }
    }

    Get-ChildItem -LiteralPath $sourceRoot -Force | ForEach-Object {
        $target = Join-Path $InstallRoot $_.Name
        if ($_.PSIsContainer) {
            New-Item -ItemType Directory -Force -Path $target | Out-Null
            Copy-Item -Path (Join-Path $_.FullName '*') -Destination $target -Recurse -Force -ErrorAction Stop
        } else {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Force
        }
    }

    foreach ($rel in $preserve) {
        $src = Join-Path $preserveRoot $rel
        $dst = Join-Path $InstallRoot $rel
        if (Test-Path -LiteralPath $src) { Copy-TreeItem $src $dst }
    }

    # Keep source/runtime Telegram settings in sync for future updates.
    $runtimeNotification = Join-Path $InstallRoot 'release\notification.json'
    $sourceNotification = Join-Path $InstallRoot 'FishingAutomation\notification.json'
    if (Test-Path $runtimeNotification) { Copy-Item $runtimeNotification $sourceNotification -Force -ErrorAction SilentlyContinue }

    Remove-Item -LiteralPath $healthy -Force -ErrorAction SilentlyContinue
    Set-Content -LiteralPath $pending -Value $token -Encoding ASCII

    Log 'Files replaced. Starting new prebuilt package for health check.'
    Start-MabiAuto

    $ok = $false
    for ($i = 0; $i -lt 90; $i++) {
        Start-Sleep -Milliseconds 500
        if (Test-Path -LiteralPath $healthy) {
            $seen = (Get-Content -LiteralPath $healthy -Raw -ErrorAction SilentlyContinue).Trim()
            if ($seen -eq $token) { $ok = $true; break }
        }
    }

    if ($ok) {
        Log 'New version startup health check OK. Update committed.'
        Remove-Item -LiteralPath $pending,$healthy -Force -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction Stop }
        if (Test-Path -LiteralPath $rollback) { Move-Item -LiteralPath $rollback -Destination $previous -Force }
        Log 'Previous version retained in .previous_version for manual rollback.'
    } else {
        Log 'New version startup health check FAILED. Rolling back.'
        foreach ($p in @('FishingAutomation','MacroWatchdog')) {
            Get-Process -Name $p -ErrorAction SilentlyContinue | ForEach-Object {
                try {
                    if ($_.Path -and $_.Path.StartsWith($InstallRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { Stop-Process -Id $_.Id -Force }
                } catch { }
            }
        }
        foreach ($name in @('release','FishingAutomation','tools')) {
            if (Test-Path -LiteralPath (Join-Path $InstallRoot $name)) { Remove-Item -LiteralPath (Join-Path $InstallRoot $name) -Recurse -Force -ErrorAction Stop }
        }
        foreach ($name in $replaceNames) {
            $src = Join-Path $rollback $name
            if (Test-Path -LiteralPath $src) { Copy-TreeItem $src (Join-Path $InstallRoot $name) }
        }
        Remove-Item -LiteralPath $pending,$healthy -Force -ErrorAction SilentlyContinue
        Start-MabiAuto
        try {
            Add-Type -AssemblyName PresentationFramework
            [System.Windows.MessageBox]::Show("새 버전 실행 확인에 실패해 이전 버전으로 자동 복구했습니다.`r`n`r`n$log", 'MABI AUTO 자동 복구') | Out-Null
        } catch { }
    }
}
catch {
    Log ("UPDATE FAILED: " + $_.Exception.Message)
    try { Stop-InstallWatchdogs } catch { }
    try {
        if ($backupComplete -and $replacementStarted -and (Test-Path -LiteralPath $rollback)) {
            Get-Process -Name FishingAutomation -ErrorAction SilentlyContinue | ForEach-Object {
                if ($_.Path -and $_.Path.StartsWith($InstallRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    Stop-Process -Id $_.Id -Force
                    $_.WaitForExit(5000) | Out-Null
                }
            }
            Remove-Item -LiteralPath $pending,$healthy -Force -ErrorAction SilentlyContinue
            foreach ($name in @('release','FishingAutomation','tools')) {
                if (Test-Path -LiteralPath (Join-Path $InstallRoot $name)) { Remove-Item -LiteralPath (Join-Path $InstallRoot $name) -Recurse -Force -ErrorAction Stop }
            }
            foreach ($name in $replaceNames) {
                $src = Join-Path $rollback $name
                if (Test-Path -LiteralPath $src) { Copy-TreeItem $src (Join-Path $InstallRoot $name) }
            }
            Start-MabiAuto
            Log 'Rollback after updater exception completed.'
        }
    } catch { Log ('Rollback failed: ' + $_.Exception.Message) }
    try {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show("MABI AUTO 업데이트 실패`r`n`r`n$($_.Exception.Message)`r`n`r`n가능하면 이전 버전으로 복구했습니다.`r`n$log", 'MABI AUTO 업데이트') | Out-Null
    } catch { }
}
finally {
    Start-Sleep -Seconds 2
    try { Remove-Item -LiteralPath $ZipPath -Force -ErrorAction SilentlyContinue } catch { }
    try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}
