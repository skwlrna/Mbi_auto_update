@echo off
chcp 65001 >nul
cd /d "%~dp0"
title MABI AUTO V0.1.76 NEW GITHUB MIGRATION
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0PUSH_TO_NEW_GITHUB.ps1"
set ERR=%ERRORLEVEL%
echo.
if not "%ERR%"=="0" (
  echo 문제가 생기면 GITHUB_MIGRATION_LOG.txt 파일을 ChatGPT에 보내주세요.
)
pause
exit /b %ERR%
