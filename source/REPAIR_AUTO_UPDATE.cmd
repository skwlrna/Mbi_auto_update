@echo off
setlocal
cd /d "%~dp0"
set "LOG=%~dp0auto_update_repair.log"
echo [MABI AUTO] Repair started.>"%LOG%"
if not exist "%~dp0tools\ApplyUpdate.ps1" (
  echo ERROR: tools\ApplyUpdate.ps1 not found.>>"%LOG%"
  echo [MABI AUTO] tools\ApplyUpdate.ps1 not found.
  exit /b 1
)
if not exist "%~dp0release\FishingAutomation.exe" (
  echo ERROR: release\FishingAutomation.exe not found.>>"%LOG%"
  echo [MABI AUTO] release\FishingAutomation.exe not found.
  exit /b 1
)
if not exist "%~dp0FishingAutomation" mkdir "%~dp0FishingAutomation"
if errorlevel 1 (
  echo ERROR: compatibility folder creation failed.>>"%LOG%"
  echo [MABI AUTO] compatibility folder creation failed.
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$p=[IO.Path]::GetFullPath('tools\ApplyUpdate.ps1');$s=[IO.File]::ReadAllText($p,[Text.Encoding]::UTF8);$enc=New-Object System.Text.UTF8Encoding -ArgumentList $true;[IO.File]::WriteAllText($p,$s,$enc)"
if errorlevel 1 (
  echo ERROR: updater encoding repair failed.>>"%LOG%"
  echo [MABI AUTO] updater encoding repair failed.
  exit /b 1
)
> "%~dp0START.cmd" echo @echo off
>>"%~dp0START.cmd" echo setlocal
>>"%~dp0START.cmd" echo cd /d "%%~dp0"
>>"%~dp0START.cmd" echo start "" "%%~dp0release\FishingAutomation.exe"
>>"%~dp0START.cmd" echo exit /b 0
echo OK: updater UTF-8 BOM, START.cmd, and compatibility folder repaired.>>"%LOG%"
echo [MABI AUTO] Auto-update repair complete.
echo Close this window, then start MABI AUTO again.
exit /b 0
