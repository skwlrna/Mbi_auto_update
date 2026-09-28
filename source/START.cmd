@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0release\FishingAutomation.exe" (
  echo [MABI AUTO] release\FishingAutomation.exe not found.
  exit /b 1
)
start "" "%~dp0release\FishingAutomation.exe"
exit /b 0
