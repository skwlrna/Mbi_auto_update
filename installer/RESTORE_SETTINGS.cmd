@echo off
setlocal
cd /d "%~dp0"
echo Enter the full path to the backup ZIP from before formatting Windows.
set /p "BACKUP=Backup ZIP path: "
if "%BACKUP%"=="" exit /b 1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0support\Restore-MabiAuto.ps1" -InstallRoot "%~dp0" -BackupPath "%BACKUP%"
set "STATUS=%ERRORLEVEL%"
echo.
if not "%STATUS%"=="0" echo Restore failed. Read the error above.
pause
exit /b %STATUS%
