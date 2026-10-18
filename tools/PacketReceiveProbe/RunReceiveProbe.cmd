@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0PacketReceiveProbe.exe" (
  echo PacketReceiveProbe.exe was not found in this folder.
  echo Download and extract the full diagnostic artifact ZIP first.
  pause
  exit /b 2
)
"%~dp0PacketReceiveProbe.exe" --interactive
set "status=%ERRORLEVEL%"
echo.
if not "%status%"=="0" echo Diagnostic ended with error code %status%.
echo No automatic capture runs after this window closes.
pause
exit /b %status%
