@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Package-Release.ps1" -ConfirmRuntimeValidated %*
exit /b %ERRORLEVEL%
