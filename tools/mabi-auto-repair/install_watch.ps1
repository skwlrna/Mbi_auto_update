#requires -Version 5.1
<#
Registers a 5-minute watcher for the current Windows user. Does not elevate.
Run from the checked-out repository:
 powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\mabi-auto-repair\install_watch.ps1 -EnablePaidAnalysis
No Codex call occurs during installation. Existing incidents become baseline.
Paid analysis applies ONLY to new same-version incidents, when available.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$LogRoot = "",
    [string]$TaskName = "MabiAutoRepair-Watch",
    [switch]$EnablePaidAnalysis,
    [switch]$EnableAutoPR
)
$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}
if (-not $LogRoot) {
    $LogRoot = Join-Path $HOME "Mbi_auto_log"
}
$RepoRoot = (Resolve-Path $RepoRoot).Path
$LogRoot = (Resolve-Path $LogRoot).Path
$watcher = Join-Path $PSScriptRoot "watch.py"
if (-not (Test-Path -PathType Leaf $watcher)) {
    throw "watch.py not found: update the Mbi_auto_update Git checkout first"
}
$python = (Get-Command python -ErrorAction Stop).Source
if (-not (Get-Command git -ErrorAction SilentlyContinue)) { throw "Git required" }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw ".NET 8 SDK required" }
if ($EnablePaidAnalysis -and -not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw "Codex CLI is not available in PATH"
}
if ($EnableAutoPR) {
    if (-not $EnablePaidAnalysis) { throw "-EnableAutoPR requires -EnablePaidAnalysis" }
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI gh is required for draft PR creation"
    }
    & gh auth status
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI gh is not logged in; run gh auth login first"
    }
}
# The baseline ensures historical incidents are NOT billed for.
& $python $watcher init --log-root $LogRoot
if ($LASTEXITCODE -ne 0) {
    throw "Watcher initialization failed. No scheduled task was installed."
}
$parameters = ('"{0}" poll --log-root "{1}" --repo "{2}"' -f $watcher, $LogRoot, $RepoRoot)
if ($EnablePaidAnalysis) {
    # Explicit install-time consent, not an implicit background paid call.
    $parameters += " --execute --acknowledge-cost"
}
if ($EnableAutoPR) {
    $parameters += " --auto-pr"
}
$action = New-ScheduledTaskAction -Execute $python -Argument $parameters -WorkingDirectory $RepoRoot
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 5) -RepetitionDuration (New-TimeSpan -Days 3650)
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
$settings = New-ScheduledTaskSettingsSet -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 25) -StartWhenAvailable
Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host ("Installed Windows task: {0} (every 5 minutes while logged on)" -f $TaskName)
Write-Host ("Paid recurring Codex analysis: {0}" -f [bool]$EnablePaidAnalysis)
Write-Host ("Automatic DRAFT PRs: {0}" -f [bool]$EnableAutoPR)
Write-Host ("See results: python .\tools\mabi-auto-repair\watch.py status")
Write-Host ('Stop: Unregister-ScheduledTask -TaskName "{0}" -Confirm:$false' -f $TaskName)
