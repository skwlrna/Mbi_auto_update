@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0FishingAutomation.exe" (
  echo Stage16 preview EXE is missing. Extract the entire ZIP into a NEW folder.
  pause
  exit /b 2
)
echo ===============================================
echo Stage 16 - Isolated read-only CLI test GUI
echo ===============================================
echo Close your normal MabiAuto macro BEFORE using this test.
echo This test does not start bots, updater, Telegram or Codex.
echo Opening the GUI performs NO query.
echo Only the GUI's "CLI 1회 확인" button will query the game CLI.
echo The test does not save or upload any reports.
echo.
choice /C YN /N /M "Open isolated read-only diagnostic window? [Y/N]: "
if errorlevel 2 (
  echo Cancelled. Nothing launched.
  exit /b 0
)
"%~dp0FishingAutomation.exe"
set "status=%ERRORLEVEL%"
echo Preview closed. status=%status%
pause
exit /b %status%
