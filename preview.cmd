@echo off
setlocal
cd /d "%~dp0"

where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo [TwinQuota] .NET SDK was not found. Install .NET 10 SDK and try again.
    exit /b 1
)

rem A running preview can keep the previous executable locked and make it look stale.
taskkill /IM TwinQuota.exe /F >nul 2>&1

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1"
if errorlevel 1 (
    echo [TwinQuota] Build failed. The preview was not started.
    exit /b 1
)

start "" "%~dp0artifacts\win-x64\app\TwinQuota.exe"
echo [TwinQuota] Preview started.
