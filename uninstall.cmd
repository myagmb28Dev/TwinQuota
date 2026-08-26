@echo off
setlocal

set "UNINSTALLER=%LOCALAPPDATA%\Programs\TwinQuota\uninstall.ps1"
if not exist "%UNINSTALLER%" (
    echo [TwinQuota] TwinQuota is not installed for this user.
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%UNINSTALLER%"
exit /b %ERRORLEVEL%
