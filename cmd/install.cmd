@echo off
setlocal
cd /d "%~dp0.."

where dotnet.exe >nul 2>&1
if errorlevel 1 (
    echo [TwinQuota] .NET SDK was not found. Install .NET 10 SDK and try again.
    exit /b 1
)

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0..\scripts\install.ps1"
if errorlevel 1 (
    echo [TwinQuota] Installation failed.
    exit /b 1
)

echo [TwinQuota] Installation completed.
