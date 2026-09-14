@echo off
setlocal
cd /d "%~dp0.."
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Validate-Build.ps1" %*
exit /b %ERRORLEVEL%
