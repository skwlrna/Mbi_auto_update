@echo off
setlocal
cd /d "%~dp0"
if not exist "%~dp0.previous_version\release\FishingAutomation.exe" (
  echo [MABI AUTO] Previous version backup was not found.
  pause
  exit /b 2
)
if not exist "%~dp0tools\RollbackPrevious.ps1" (
  echo [MABI AUTO] tools\RollbackPrevious.ps1 was not found.
  pause
  exit /b 3
)
echo.
echo [MABI AUTO] Restore the previous version?
echo Current settings, templates, Telegram settings and interception.dll will be preserved.
choice /C YN /N /M "Continue? [Y/N] "
if errorlevel 2 exit /b 0
set "TMPPS=%TEMP%\MabiAutoRollback_%RANDOM%_%RANDOM%.ps1"
copy /Y "%~dp0tools\RollbackPrevious.ps1" "%TMPPS%" >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%TMPPS%" -InstallRoot "%~dp0"
set "ERR=%ERRORLEVEL%"
del /Q "%TMPPS%" >nul 2>nul
if not "%ERR%"=="0" pause
exit /b %ERR%
