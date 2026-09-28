param(
    [Parameter(Mandatory=$true)][string]$InstallRoot
)

$ErrorActionPreference = 'Stop'
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$previous = Join-Path $InstallRoot '.previous_version'
$log = Join-Path $InstallRoot 'rollback.log'
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$work = Join-Path $env:TEMP "MabiAutoRollback_$stamp"
$current = Join-Path $work 'current'
$preserveRoot = Join-Path $work 'preserve'

function Log([string]$text) {
    if ((Test-Path -LiteralPath $log) -and (Get-Item -LiteralPath $log).Length -gt 5MB) { Clear-Content -LiteralPath $log }
    "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $text" | Tee-Object -FilePath $log -Append | Out-Null
}

function Copy-TreeItem([string]$src, [string]$dst) {
    if (-not (Test-Path -LiteralPath $src)) { return }
    $item = Get-Item -LiteralPath $src
    if ($item.PSIsContainer) {
        New-Item -ItemType Directory -Force -Path $dst | Out-Null
        Get-ChildItem -LiteralPath $src -Force | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $dst -Recurse -Force -ErrorAction Stop
        }
    } else {
        New-Item -ItemType Directory -Force -Path (Split-Path $dst -Parent) | Out-Null
        Copy-Item -LiteralPath $src -Destination $dst -Force
    }
}

function Stop-InstallProcesses {
    foreach ($name in @('FishingAutomation','MacroWatchdog')) {
        Get-Process -Name $name -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $path = $_.Path
                if ($path -and $path.StartsWith($InstallRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                    Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
                    try { $_.WaitForExit(5000) } catch { }
                }
            } catch { }
        }
    }
    Start-Sleep -Milliseconds 500
}

function Start-MabiAuto {
    $startCmd = Join-Path $InstallRoot 'START.cmd'
    if (Test-Path -LiteralPath $startCmd) {
        Start-Process -FilePath $startCmd -WorkingDirectory $InstallRoot
        return
    }
    $exe = Join-Path $InstallRoot 'release\FishingAutomation.exe'
    if (Test-Path -LiteralPath $exe) {
        Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe -Parent)
        return
    }
    throw '복구 후 실행 파일을 찾지 못했습니다.'
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
    if (-not (Test-Path -LiteralPath (Join-Path $previous 'release\FishingAutomation.exe'))) {
        throw '보관된 이전 버전(.previous_version)이 없습니다.'
    }

    New-Item -ItemType Directory -Force -Path $work,$current,$preserveRoot | Out-Null
    Log 'Manual rollback start.'

    foreach ($rel in $preserve) { Copy-TreeItem (Join-Path $InstallRoot $rel) (Join-Path $preserveRoot $rel) }
    foreach ($name in $replaceNames) { Copy-TreeItem (Join-Path $InstallRoot $name) (Join-Path $current $name) }

    Stop-InstallProcesses

    foreach ($name in @('release','FishingAutomation','tools')) {
        $target = Join-Path $InstallRoot $name
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop }
    }
    foreach ($name in $replaceNames) {
        $src = Join-Path $previous $name
        if (Test-Path -LiteralPath $src) { Copy-TreeItem $src (Join-Path $InstallRoot $name) }
    }

    foreach ($rel in $preserve) {
        $src = Join-Path $preserveRoot $rel
        if (Test-Path -LiteralPath $src) { Copy-TreeItem $src (Join-Path $InstallRoot $rel) }
    }

    $restoredExe = Join-Path $InstallRoot 'release\FishingAutomation.exe'
    if (-not (Test-Path -LiteralPath $restoredExe)) { throw '이전 버전 실행 파일 복구 검증에 실패했습니다.' }

    Remove-Item -LiteralPath $previous -Recurse -Force -ErrorAction Stop
    Move-Item -LiteralPath $current -Destination $previous -Force

    Remove-Item -LiteralPath (Join-Path $InstallRoot '.update_pending'),(Join-Path $InstallRoot '.update_healthy'),(Join-Path $InstallRoot '.update_rollback') -Recurse -Force -ErrorAction SilentlyContinue
    Log 'Manual rollback completed. Current version was retained as the next previous_version backup.'
    Start-MabiAuto
    exit 0
}
catch {
    Log ('ROLLBACK FAILED: ' + $_.Exception.Message)
    try {
        if (Test-Path -LiteralPath (Join-Path $current 'release\FishingAutomation.exe')) {
            Stop-InstallProcesses
            foreach ($name in @('release','FishingAutomation','tools')) {
                $target = Join-Path $InstallRoot $name
                if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction SilentlyContinue }
            }
            foreach ($name in $replaceNames) {
                $src = Join-Path $current $name
                if (Test-Path -LiteralPath $src) { Copy-TreeItem $src (Join-Path $InstallRoot $name) }
            }
            foreach ($rel in $preserve) {
                $src = Join-Path $preserveRoot $rel
                if (Test-Path -LiteralPath $src) { Copy-TreeItem $src (Join-Path $InstallRoot $rel) }
            }
            Log 'Rollback failure recovery restored the version that was running before rollback.'
            Start-MabiAuto
        }
    } catch { Log ('Rollback failure recovery also failed: ' + $_.Exception.Message) }
    try {
        Add-Type -AssemblyName PresentationFramework
        [System.Windows.MessageBox]::Show("이전 버전 복구에 실패했습니다.`r`n`r`n$($_.Exception.Message)`r`n`r`n$log", 'MABI AUTO 롤백') | Out-Null
    } catch { }
    exit 1
}
finally {
    try { Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue } catch { }
}
