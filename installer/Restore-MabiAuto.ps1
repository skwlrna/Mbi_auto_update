# Restores only explicit configuration, templates and safe restart journals.
# Requires Mabi Auto to be closed; makes a safety backup before changing anything.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$BackupPath,
    [string]$InstallRoot = "",
    [switch]$NoPrompt
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $InstallRoot) { $InstallRoot = Split-Path -Parent $PSScriptRoot }
$InstallRoot = [IO.Path]::GetFullPath($InstallRoot)
$BackupPath = [IO.Path]::GetFullPath($BackupPath)
if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) { throw "Backup ZIP not found: $BackupPath" }
if (-not (Test-Path -LiteralPath (Join-Path $InstallRoot 'release\FishingAutomation.exe'))) {
    throw "Install Mabi Auto before restoring: $InstallRoot"
}
if (Get-Process FishingAutomation -ErrorAction SilentlyContinue) {
    throw 'Close Mabi Auto before restoring. Do not replace active job state.'
}
Add-Type -AssemblyName System.IO.Compression
$archive = [IO.Compression.ZipFile]::OpenRead($BackupPath)
try {
    if ($archive.Entries.Count -gt 15000) { throw 'Backup contains too many files' }
    $total = [long]0
    foreach ($entry in $archive.Entries) {
        $name = $entry.FullName.Replace('\','/')
        if ($name.StartsWith('/') -or $name.Contains(':') -or
            $name -match '(^|/)\.\.(/|$)' -or $name -match '(^|/)\.(/|$)') {
            throw "Unsafe ZIP entry: $name"
        }
        if ($name -and $name -ne 'program/' -and $name -ne 'local/' -and $name -ne 'local/MabiAuto/' -and
            $name -notmatch '^(manifest\.json|program/release/|local/MabiAuto/)') {
            throw "Unexpected ZIP entry: $name"
        }
        $total += $entry.Length
        if ($total -gt 1073741824) { throw 'Uncompressed backup is larger than 1 GiB' }
    }
} finally { $archive.Dispose() }
$stage = Join-Path $env:TEMP ("MabiAutoRestore_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
function Copy-AllowedFile([string]$root, [string]$relative, [string]$destinationRoot) {
    $source = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { return }
    $dest = Join-Path $destinationRoot $relative
    New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $dest -Force
}
function Copy-AllowedDirectory([string]$root, [string]$relative, [string]$destinationRoot, [bool]$journalOnly) {
    $source = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $source -PathType Container)) { return }
    $destination = Join-Path $destinationRoot $relative
    foreach ($file in (Get-ChildItem -LiteralPath $source -File -Recurse)) {
        if ($journalOnly) {
            if ($file.Extension -ine '.json' -and $file.Name -ine 'recent-hidden.txt') { continue }
        } elseif ($file.Extension -inotmatch '^\.(png|jpg|jpeg|bmp)$') {
            continue
        }
        $subpath = $file.FullName.Substring($source.Length).TrimStart('\')
        $dest = Join-Path $destination $subpath
        New-Item -ItemType Directory -Path (Split-Path -Parent $dest) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
    }
}
try {
    Expand-Archive -LiteralPath $BackupPath -DestinationPath $stage
    $manifestPath = Join-Path $stage 'manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw 'Missing backup manifest' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.Kind -ne 'MabiAutoBackup' -or $manifest.Format -ne 1) {
        throw 'Unsupported or invalid Mabi Auto backup'
    }
    if (-not $NoPrompt) {
        Write-Host 'Existing settings may be replaced. Unfinished jobs will still require in-game revalidation.' -ForegroundColor Yellow
        $answer = Read-Host 'Type RESTORE to continue'
        if ($answer -cne 'RESTORE') { throw 'Restore cancelled' }
    }
    $docs = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
    if (-not $docs) { $docs = $env:USERPROFILE }
    $safetyBackup = Join-Path $docs ("MabiAuto_BeforeRestore_{0}.zip" -f (Get-Date -Format 'yyyyMMdd_HHmmss_fff'))
    & (Join-Path $PSScriptRoot 'Backup-MabiAuto.ps1') -InstallRoot $InstallRoot -OutputPath $safetyBackup
    $program = Join-Path $stage 'program'
    foreach ($name in @(
        'release\config.json', 'release\notification.json',
        'release\dungeon\config\appsettings.json', 'release\dungeon\config\scenario.json',
        'release\dungeon\config\targets.json', 'release\abyss\config\appsettings.json',
        'release\abyss\config\scenario.json', 'release\abyss\config\targets.json')) {
        Copy-AllowedFile $program $name $InstallRoot
    }
    foreach ($name in @('release\templates', 'release\dungeon\templates', 'release\abyss\templates')) {
        Copy-AllowedDirectory $program $name $InstallRoot $false
    }
    $local = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MabiAuto'
    $localSource = Join-Path $stage 'local\MabiAuto'
    foreach ($name in @('auto-stop.json', 'error-upload.json')) {
        Copy-AllowedFile $localSource $name $local
    }
    foreach ($name in @('multi-altering', 'crafting')) {
        Copy-AllowedDirectory $localSource $name $local $true
    }
    # Reject credentials from any externally modified ZIP: credentials are never restored.
    $notification = Join-Path $InstallRoot 'release\notification.json'
    if (Test-Path -LiteralPath $notification) {
        $settings = Get-Content -LiteralPath $notification -Raw | ConvertFrom-Json
        foreach ($kv in @(@('BotToken',''),@('ChatId',''),@('Enabled',$false),@('RemoteControlEnabled',$false))) {
            $settings | Add-Member -NotePropertyName $kv[0] -NotePropertyValue $kv[1] -Force
        }
        $settings | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $notification -Encoding UTF8
    }
    $upload = Join-Path $local 'error-upload.json'
    if (Test-Path -LiteralPath $upload) {
        $prefs = Get-Content -LiteralPath $upload -Raw | ConvertFrom-Json
        $prefs | Add-Member -NotePropertyName 'ProtectedToken' -NotePropertyValue '' -Force
        $prefs | Add-Member -NotePropertyName 'Enabled' -NotePropertyValue $false -Force
        $prefs | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $upload -Encoding UTF8
    }
    Write-Host 'Restore completed. Review queued jobs before starting them.' -ForegroundColor Green
    Write-Host "Pre-restore safety backup: $safetyBackup"
    Write-Warning 'Re-enter Telegram and GitHub credentials. Verify Interception, Korean OCR and the separate game CLI.'
} finally {
    Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction SilentlyContinue
}
