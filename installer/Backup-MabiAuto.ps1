# Backs up mutable settings and restart journals, never executable code or tokens.
[CmdletBinding()]
param([string]$InstallRoot = "", [string]$OutputPath = "")
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $InstallRoot) { $InstallRoot = Split-Path -Parent $PSScriptRoot }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
if (-not (Test-Path -LiteralPath (Join-Path $InstallRoot 'release\FishingAutomation.exe'))) {
    throw "Mabi Auto installation not found: $InstallRoot"
}
if (Get-Process FishingAutomation -ErrorAction SilentlyContinue) {
    throw 'Close Mabi Auto and stop active jobs before backing up.'
}
if (-not $OutputPath) {
    $docs = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    if (-not $docs) { $docs = $env:USERPROFILE }
    $OutputPath = Join-Path $docs ("MabiAuto_Backup_{0}.zip" -f (Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
}
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not $OutputPath.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) { throw 'Backup name must end in .zip' }
if (Test-Path -LiteralPath $OutputPath) { throw "Refusing to overwrite an existing backup: $OutputPath" }
$stage = Join-Path $env:TEMP ("MabiAutoBackup_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
function Copy-BackupFile([string]$root, [string]$relative, [string]$prefix) {
    $source = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { return }
    $dest = Join-Path $stage (Join-Path $prefix $relative)
    New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $dest -Force
}
function Copy-BackupDirectory([string]$root, [string]$relative, [string]$prefix, [bool]$journalOnly) {
    $source = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { return }
    $dest = Join-Path $stage (Join-Path $prefix $relative)
    foreach ($file in (Get-ChildItem -LiteralPath $source -File -Recurse)) {
        if ($journalOnly -and $file.Extension -ine '.json' -and $file.Name -ine 'recent-hidden.txt') { continue }
        $subpath = $file.FullName.Substring($source.Length).TrimStart('\')
        $target = Join-Path $dest $subpath
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
}
try {
    foreach ($name in @(
        'release\config.json', 'release\notification.json',
        'release\dungeon\config\appsettings.json',
        'release\dungeon\config\scenario.json', 'release\dungeon\config\targets.json',
        'release\abyss\config\appsettings.json',
        'release\abyss\config\scenario.json', 'release\abyss\config\targets.json')) {
        Copy-BackupFile $InstallRoot $name 'program'
    }
    foreach ($name in @('release\templates', 'release\dungeon\templates', 'release\abyss\templates')) {
        Copy-BackupDirectory $InstallRoot $name 'program' $false
    }
    $notification = Join-Path $stage 'program\release\notification.json'
    if (Test-Path -LiteralPath $notification) {
        $settings = Get-Content -LiteralPath $notification -Raw | ConvertFrom-Json
        foreach ($kv in @(@('BotToken',''),@('ChatId',''),@('Enabled',$false),@('RemoteControlEnabled',$false))) {
            $settings | Add-Member -NotePropertyName $kv[0] -NotePropertyValue $kv[1] -Force
        }
        $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $notification -Encoding UTF8
    }
    $local = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MabiAuto'
    foreach ($name in @('multi-altering', 'crafting')) {
        Copy-BackupDirectory $local $name 'local\MabiAuto' $true
    }
    Copy-BackupFile $local 'auto-stop.json' 'local\MabiAuto'
    # The per-Windows-user DPAPI-protected PAT cannot be imported after a reformat.
    $upload = Join-Path $local 'error-upload.json'
    if (Test-Path -LiteralPath $upload) {
        $prefs = Get-Content -LiteralPath $upload -Raw | ConvertFrom-Json
        $prefs | Add-Member -NotePropertyName 'ProtectedToken' -NotePropertyValue '' -Force
        $prefs | Add-Member -NotePropertyName 'Enabled' -NotePropertyValue $false -Force
        $dest = Join-Path $stage 'local\MabiAuto\error-upload.json'
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        $prefs | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $dest -Encoding UTF8
    }
    [ordered]@{
        Kind = 'MabiAutoBackup'
        Format = 1
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        Notes = 'Telegram Bot Token, Chat ID and GitHub PAT must be re-entered after restore.'
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'manifest.json') -Encoding UTF8
    New-Item -ItemType Directory -Path (Split-Path -Parent $OutputPath) -Force | Out-Null
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $OutputPath -CompressionLevel Optimal
    Write-Host "Backup saved: $OutputPath" -ForegroundColor Green
    Write-Warning 'Keep this ZIP private. It may contain character, account and job details. Never upload to public GitHub.'
    Write-Warning 'Telegram credentials and GitHub upload tokens have been excluded.'
} finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
