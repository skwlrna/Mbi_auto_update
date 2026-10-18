@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0PacketReceiveProbe.exe" (
  echo PacketReceiveProbe.exe is missing from this folder.
  echo Extract the entire ZIP, then run this launcher again.
  pause
  exit /b 2
)
echo One-run, three-stage diagnostic: client closed, idle and manual activity.
echo No capture or game CLI query starts without your explicit YES consent.
"%~dp0PacketReceiveProbe.exe" --guided
set "status=%ERRORLEVEL%"
echo.
if not "%status%"=="0" echo Diagnostic ended with code %status%.
echo This utility does not run in the background.
pause
exit /b %status%
