$ErrorActionPreference = 'Stop'

function Test-Admin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Admin)) {
    Write-Host 'Administrator permission is required. Opening an elevated PowerShell...'
    Start-Process powershell.exe -Verb RunAs -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"')
    )
    exit
}

$root = Split-Path $PSScriptRoot -Parent
$projectDir = Join-Path $root 'FishingAutomation'
$tempRoot = Join-Path $env:TEMP ('FishingAutomation_Interception_' + [guid]::NewGuid().ToString('N'))
$zip = Join-Path $tempRoot 'Interception.zip'
$extract = Join-Path $tempRoot 'unpacked'
$url = 'https://github.com/oblitum/Interception/releases/download/v1.0.1/Interception.zip'

Write-Host ''
Write-Host 'This installs the official Interception v1.0.1 keyboard/mouse filter driver.' -ForegroundColor Yellow
Write-Host 'A Windows reboot is required after installation.' -ForegroundColor Yellow
Write-Host ''
$answer = Read-Host 'Type YES to continue'
if ($answer -ne 'YES') {
    Write-Host 'Cancelled.'
    Read-Host 'Press Enter to close'
    exit
}

New-Item -ItemType Directory -Force -Path $tempRoot, $extract | Out-Null
Write-Host 'Downloading official Interception package...'
Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $zip
Write-Host 'Extracting...'
Expand-Archive -Path $zip -DestinationPath $extract -Force

$installer = Get-ChildItem $extract -Recurse -Filter 'install-interception.exe' | Select-Object -First 1
$dll = Get-ChildItem $extract -Recurse -Filter 'interception.dll' | Where-Object { $_.FullName -match '[\\/]library[\\/]x64[\\/]interception\.dll$' } | Select-Object -First 1

if (-not $installer) { throw 'install-interception.exe was not found in the official package.' }
if (-not $dll) { throw 'x64 interception.dll was not found in the official package.' }

Write-Host 'Installing driver...'
$p = Start-Process -FilePath $installer.FullName -ArgumentList '/install' -Wait -PassThru -NoNewWindow
Write-Host ('Installer exit code: ' + $p.ExitCode)

Copy-Item $dll.FullName (Join-Path $projectDir 'interception.dll') -Force
$releaseDir = Join-Path $root 'release'
if (Test-Path $releaseDir) {
    Copy-Item $dll.FullName (Join-Path $releaseDir 'interception.dll') -Force
}

Write-Host ''
Write-Host 'Interception DLL copied and driver install command completed.' -ForegroundColor Green
Write-Host 'IMPORTANT: Reboot Windows now. After reboot run START.cmd.' -ForegroundColor Cyan
Write-Host 'Then press F8 while the fishing icon is visible. The game should react to Space.' -ForegroundColor Cyan
Write-Host ''
Read-Host 'Press Enter to close'
