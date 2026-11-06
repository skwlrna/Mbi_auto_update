@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0support\Backup-MabiAuto.ps1" -InstallRoot "%~dp0"
set "STATUS=%ERRORLEVEL%"
echo.
if not "%STATUS%"=="0" echo Backup failed. Read the error above.
pause
exit /b %STATUS%
