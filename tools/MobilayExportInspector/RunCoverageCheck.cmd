@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0MobilayExportInspector.exe" (
  echo MobilayExportInspector.exe is missing.
  echo Extract the entire ZIP first.
  pause
  exit /b 2
)
"%~dp0MobilayExportInspector.exe" --coverage
echo.
echo This is an audited policy map, not proof of Mobilay live event support.
pause
