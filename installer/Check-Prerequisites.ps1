# Read-only by default; optionally install Windows Korean OCR when explicitly requested.
[CmdletBinding()]
param([string]$InstallRoot = "", [switch]$InstallKoreanOCR)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $InstallRoot) { $InstallRoot = Split-Path -Parent $PSScriptRoot }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$release = Join-Path $InstallRoot 'release'
$required = @(
    @{ Name='Main EXE (.NET 8 self-contained)'; Path=(Join-Path $release 'FishingAutomation.exe') },
    @{ Name='Watchdog'; Path=(Join-Path $release 'MacroWatchdog.exe') },
    @{ Name='Interception DLL'; Path=(Join-Path $release 'interception.dll') },
    @{ Name='Update helper'; Path=(Join-Path $InstallRoot 'tools\ApplyUpdate.ps1') },
    @{ Name='Dungeon settings'; Path=(Join-Path $release 'dungeon\config\appsettings.json') },
    @{ Name='Abyss settings'; Path=(Join-Path $release 'abyss\config\appsettings.json') }
)
$missing = 0
foreach ($item in $required) {
    $exists = Test-Path -LiteralPath $item.Path -PathType Leaf
    Write-Host ("[{0}] {1}" -f $(if($exists){'OK'}else{'MISSING'}), $item.Name)
    if (-not $exists) { $missing++ }
}
$cli = 'C:\Nexon\MabinogiMobile\MabinogiMobile_CLI.exe'
if (Test-Path -LiteralPath $cli -PathType Leaf) {
    Write-Host '[OK] Game CLI executable exists.'
} else {
    Write-Warning "Game CLI missing: $cli. This external file is not distributed with Mabi Auto."
}
$ocrName = 'Language.OCR~~~ko-KR~0.0.1.0'
try {
    $cap = Get-WindowsCapability -Online -Name $ocrName -ErrorAction Stop
    if ($cap.State -eq 'Installed') {
        Write-Host '[OK] Windows Korean OCR capability installed.'
    } elseif ($InstallKoreanOCR) {
        Write-Host 'Installing Windows Korean OCR from Windows Update...'
        $result = Add-WindowsCapability -Online -Name $ocrName -ErrorAction Stop
        Write-Host "Korean OCR installation restart needed: $($result.RestartNeeded)"
        if ($result.RestartNeeded) { Write-Warning 'Restart Windows to complete OCR installation.' }
    } else {
        Write-Warning 'Korean OCR missing. Install in Windows Language settings or rerun with -InstallKoreanOCR.'
    }
} catch {
    Write-Warning ("Korean OCR check/install unavailable: " + $_.Exception.Message)
}
if ([Environment]::OSVersion.Version.Build -lt 19041) {
    Write-Warning 'Windows is older than the Windows 10 build 19041 minimum API target.'
}
Write-Warning 'Interception DLL presence does not prove that the kernel driver is installed. Install it with consent, reboot, and test actual keyboard/mouse input in the application.'
Write-Warning 'GitHub error-reporting PAT and Telegram secrets must be entered again after reformatting.'
if ($missing -ne 0) { throw "$missing required installation files are missing." }
Write-Host 'Installation files verified. Game CLI and real input still require an on-PC check.' -ForegroundColor Green
