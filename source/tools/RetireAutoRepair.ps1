# V3.2.64 automatic retirement, executed by the newly installed Windows app.
# No game process, general updater, runtime error archive or Git source is deleted.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$homeRoot = [Environment]::GetFolderPath('UserProfile')
$taskName = 'MabiAutoRepair-Watch'
$logRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'MabiAuto'
$log = Join-Path $logRoot 'auto_repair_retirement.log'
New-Item -ItemType Directory -Path $logRoot -Force | Out-Null
function Write-Result([string]$message) {
    "[$(Get-Date -Format o)] $message" | Add-Content -LiteralPath $log -Encoding UTF8
}
try {
    $schtasks = Join-Path $env:SystemRoot 'System32\schtasks.exe'
    if (-not (Test-Path -LiteralPath $schtasks)) { throw 'Windows Task Scheduler CLI missing' }
    & $schtasks /Query /TN $taskName 1>$null 2>$null
    if ($LASTEXITCODE -eq 0) {
        & $schtasks /End /TN $taskName 1>$null 2>$null
        & $schtasks /Delete /TN $taskName /F 1>$null 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Cannot delete named auto-repair scheduled task' }
        Write-Result "Task removed: $taskName"
    }

    # Only terminate Python scripts from the retired dedicated repair tool.
    $workers = @(Get-CimInstance Win32_Process -Filter "Name='python.exe' OR Name='pythonw.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match 'mabi-auto-repair[\\/](watch|repair)\.py' })
    foreach ($worker in $workers) {
        Stop-Process -Id $worker.ProcessId -Force -ErrorAction Stop
        Write-Result "Retired worker ended: $($worker.ProcessId)"
    }
    foreach ($leaf in @('.MabiAutoRepair','MabiAutoAI')) {
        $target = Join-Path $homeRoot $leaf
        if (-not (Test-Path -LiteralPath $target)) { continue }
        $item = Get-Item -LiteralPath $target -Force -ErrorAction Stop
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing reparse-point deletion: $leaf"
        }
        Remove-Item -LiteralPath $target -Recurse -Force -ErrorAction Stop
        Write-Result "Obsolete owned data removed: $leaf"
    }
    Write-Result 'Dedicated automatic repair retirement complete; game logs retained.'
    exit 0
} catch {
    Write-Result ("Retirement incomplete: " + $_.Exception.Message)
    exit 1
}
