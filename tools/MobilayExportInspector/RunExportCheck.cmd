@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0MobilayExportInspector.exe" (
  echo Offline export inspector EXE missing from this folder.
  pause
  exit /b 2
)
if "%~1"=="" (
  echo Drag ONE Mobilay exported JSON or CSV file onto this RunExportCheck.cmd.
  echo No file opened; nothing was captured.
  pause
  exit /b 2
)
"%~dp0MobilayExportInspector.exe" --inspect "%~1"
echo.
echo Structure summary only; not proof of an external live event API.
pause
