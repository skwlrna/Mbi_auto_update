#requires -Version 5.1
<#
Read-only PC readiness check. Does not start a macro, patch code, spend AI
tokens, merge pull requests, update binaries, or modify scheduled tasks.
Run: powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\mabi-auto-repair\doctor.ps1
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = "",
    [string]$TaskName = "MabiAutoRepair-Watch"
)
$ErrorActionPreference = 'Stop'
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
}
$issues = New-Object 'System.Collections.Generic.List[string]'
$warnings = New-Object 'System.Collections.Generic.List[string]'
$repo = $null
try { $repo = (Resolve-Path -LiteralPath $RepoRoot).Path }
catch { $issues.Add('Git repository path does not exist') }

function Has-Command([string]$name) {
    return [bool](Get-Command $name -ErrorAction SilentlyContinue)
}

$gitInfo = [ordered]@{
    repository = $repo
    branch = $null
    head = $null
    origin_main = $null
    clean = $false
    synced_with_fetched_origin = $false
}
if ($repo -and (Has-Command 'git')) {
    $gitInfo.branch = (& git -C $repo branch --show-current 2>$null | Out-String).Trim()
    $gitInfo.head = (& git -C $repo rev-parse --short HEAD 2>$null | Out-String).Trim()
    $gitInfo.origin_main = (& git -C $repo rev-parse --short origin/main 2>$null | Out-String).Trim()
    $changed = @(& git -C $repo status --porcelain --untracked-files=all 2>$null)
    if ($LASTEXITCODE -ne 0) { $issues.Add('Git status failed') }
    else { $gitInfo.clean = ($changed.Count -eq 0) }
    $gitInfo.synced_with_fetched_origin = ($gitInfo.branch -eq 'main' -and
        $gitInfo.head -and $gitInfo.head -eq $gitInfo.origin_main)
    if (-not $gitInfo.clean) { $issues.Add('Repository has uncommitted/untracked files; auto-fixer will refuse patching') }
    if (-not $gitInfo.synced_with_fetched_origin) {
        $issues.Add('Main differs from locally fetched origin/main or branch is not main')
    }
} else {
    $issues.Add('Git unavailable')
}

$taskInfo = [ordered]@{
    name = $TaskName
    exists = $false
    enabled = $false
    state = $null
    last_run = $null
    last_result = $null
    next_run = $null
    arguments_include_paid_analysis = $false
    arguments_include_auto_pr = $false
    uses_this_repository = $false
}
try {
    $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction Stop
    $taskInfo.exists = $true
    $taskInfo.state = [string]$task.State
    $taskInfo.enabled = ($task.State -ne 'Disabled')
    $action = @($task.Actions)[0]
    if ($null -eq $action) { $issues.Add('Watcher scheduled task has no executable action') }
    else {
        $argsText = [string]$action.Arguments
        $taskInfo.arguments_include_paid_analysis =
            ($argsText.Contains('--execute') -and $argsText.Contains('--acknowledge-cost'))
        $taskInfo.arguments_include_auto_pr = $argsText.Contains('--auto-pr')
        $taskInfo.uses_this_repository = [bool]($repo -and
            $argsText.Contains($repo) -and $argsText.Contains('watch.py'))
        if (-not $taskInfo.arguments_include_paid_analysis) {
            $issues.Add('Task is not set up for consented automatic analysis')
        }
        if (-not $taskInfo.arguments_include_auto_pr) {
            $issues.Add('Task is not set up to create Draft repair PRs')
        }
        if (-not $taskInfo.uses_this_repository) {
            $issues.Add('Task points to another repository or watcher script')
        }
    }
    $history = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction Stop
    $taskInfo.last_run = [string]$history.LastRunTime
    $taskInfo.last_result = $history.LastTaskResult
    $taskInfo.next_run = [string]$history.NextRunTime
    if ($history.LastTaskResult -ne 0) {
        $warnings.Add('Scheduled task last run did not return zero; inspect Task Scheduler history')
    }
    if (-not $taskInfo.enabled) { $issues.Add('Watcher task is disabled') }
} catch {
    $issues.Add('Cannot read MabiAutoRepair-Watch scheduled task')
}

$dependencies = [ordered]@{
    python = Has-Command 'python'
    dotnet = Has-Command 'dotnet'
    git = Has-Command 'git'
    github_cli = Has-Command 'gh'
    codex_cli = Has-Command 'codex'
    github_login_valid = $false
}
foreach ($name in @('python','dotnet','git','github_cli','codex_cli')) {
    if (-not $dependencies[$name]) { $issues.Add("Missing required command: $name") }
}
if ($dependencies.github_cli) {
    # Do not print gh auth output; it can contain account or credential details.
    $null = & gh auth status 2>&1
    $dependencies.github_login_valid = ($LASTEXITCODE -eq 0)
    if (-not $dependencies.github_login_valid) {
        $issues.Add('GitHub CLI is not authenticated for the scheduled Windows user')
    }
}

$watcherInfo = [ordered]@{readable = $false; baseline = $null; statuses = @{}}
$watcher = Join-Path $PSScriptRoot 'watch.py'
if ($dependencies.python -and (Test-Path -LiteralPath $watcher)) {
    try {
        $raw = & python -B $watcher status 2>$null | Out-String
        if ($LASTEXITCODE -ne 0) { throw 'watch.py returned an error' }
        $watch = $raw | ConvertFrom-Json
        if ($watch.status -ne 'watch_status') { throw 'Invalid watch status' }
        $watcherInfo.readable = $true
        $watcherInfo.statuses = $watch.counts
        $watcherInfo.baseline = $watch.counts.baseline
    } catch {
        $issues.Add('Cannot read watcher state database')
    }
} else { $issues.Add('Watcher Python script missing or Python unavailable') }

$result = [ordered]@{
    status = if ($issues.Count -eq 0) { 'ready_for_next_real_error' } else { 'attention_required' }
    local_pc_only = $true
    checks_are_read_only = $true
    live_game_or_macro_update_tested = $false
    repository = $gitInfo
    scheduled_watcher = $taskInfo
    dependencies = $dependencies
    watcher_state = $watcherInfo
    problems = @($issues.ToArray())
    warnings = @($warnings.ToArray())
    note = 'This checks prerequisites, not end-to-end successful real errors, releases or game behavior.'
}
$result | ConvertTo-Json -Depth 6
if ($issues.Count -gt 0) { exit 2 }
exit 0
