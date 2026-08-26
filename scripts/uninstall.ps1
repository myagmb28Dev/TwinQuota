[CmdletBinding()]
param(
    [string]$InstallDirectory,
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
$localProgramsRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))
if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $localProgramsRoot "TwinQuota"
}

$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
if (-not $installRoot.StartsWith($localProgramsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to uninstall a directory outside '$localProgramsRoot'."
}

Get-Process -Name "TwinQuota" -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and $_.Path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)
} | Stop-Process -Force

$installedExecutable = Join-Path $installRoot "TwinQuota.exe"
if (Test-Path -LiteralPath $installedExecutable) {
    Start-Process -FilePath $installedExecutable -ArgumentList "--unregister-antigravity-hook" -WindowStyle Hidden -Wait
}

$shortcutPath = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\TwinQuota.lnk"
if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath -Force
}

$uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TwinQuota"
if (Test-Path -LiteralPath $uninstallKey) {
    Remove-Item -LiteralPath $uninstallKey -Recurse -Force
}

if (Test-Path -LiteralPath $installRoot) {
    try {
        Remove-Item -LiteralPath $installRoot -Recurse -Force
    }
    catch {
        $escapedInstallRoot = $installRoot.Replace('"', '""')
        $cleanupCommand = "ping 127.0.0.1 -n 2 >nul & rmdir /s /q `"$escapedInstallRoot`""
        Start-Process -FilePath $env:ComSpec -ArgumentList "/d", "/c", $cleanupCommand -WindowStyle Hidden
    }
}

if (-not $Quiet) {
    Write-Host "TwinQuota was uninstalled."
}
