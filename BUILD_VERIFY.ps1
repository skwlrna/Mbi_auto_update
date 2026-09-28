$ErrorActionPreference='Stop'
Set-Location $PSScriptRoot
dotnet build source/FishingAutomation/FishingAutomation.csproj -c Release -r win-x64
if($LASTEXITCODE-ne0){throw 'App build failed'}
dotnet build source/MacroWatchdog/MacroWatchdog.csproj -c Release
if($LASTEXITCODE-ne0){throw 'Watchdog build failed'}
dotnet run --project changes/tests/stability/Regression.csproj -c Release
if($LASTEXITCODE-ne0){throw 'Production regressions failed'}
& ./changes/tests/stability/Updater.Tests.ps1 -Updater "$PSScriptRoot/source/tools/ApplyUpdate.ps1"
if($LASTEXITCODE-ne0){throw 'Updater regressions failed'}
Write-Host 'BUILD_AND_REGRESSIONS_OK (live game/device test still required)'
