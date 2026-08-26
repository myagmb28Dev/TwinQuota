[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [string]$InstallDirectory,
    [switch]$NoRestore,
    [switch]$NoLaunch,
    [switch]$SkipRegistration
)

$ErrorActionPreference = "Stop"
$repositoryRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$localProgramsRoot = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA "Programs"))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))

if ([string]::IsNullOrWhiteSpace($InstallDirectory)) {
    $InstallDirectory = Join-Path $localProgramsRoot "TwinQuota"
}

$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
$allowedRoots = @($localProgramsRoot, $artifactsRoot)
$isAllowedTarget = $allowedRoots | Where-Object {
    $installRoot.StartsWith($_ + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}
if (-not $isAllowedTarget) {
    throw "Install directory must be inside '$localProgramsRoot' or '$artifactsRoot'."
}

$publishArguments = @{ Runtime = $Runtime }
if ($NoRestore) {
    $publishArguments.NoRestore = $true
}

& (Join-Path $PSScriptRoot "publish.ps1") @publishArguments
$publishedApp = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "$Runtime\app"))
$publishedExecutable = Join-Path $publishedApp "TwinQuota.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable)) {
    throw "Published executable was not found at '$publishedExecutable'."
}

Get-Process -Name "TwinQuota" -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and (
        $_.Path.StartsWith($repositoryRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $_.Path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)
    )
} | Stop-Process -Force

if (Test-Path -LiteralPath $installRoot) {
    Remove-Item -LiteralPath $installRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
Copy-Item -Path (Join-Path $publishedApp "*") -Destination $installRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "uninstall.ps1") -Destination (Join-Path $installRoot "uninstall.ps1") -Force

$installedExecutable = Join-Path $installRoot "TwinQuota.exe"
if (-not (Test-Path -LiteralPath $installedExecutable)) {
    throw "Installed executable was not created at '$installedExecutable'."
}

$sourceIcon = Join-Path $repositoryRoot "src\TwinQuota.Windows\Assets\TwinQuota.ico"
if (-not (Test-Path -LiteralPath $sourceIcon)) {
    throw "Application icon was not found at '$sourceIcon'."
}

$iconHash = (Get-FileHash -LiteralPath $sourceIcon -Algorithm SHA256).Hash.Substring(0, 12).ToLowerInvariant()
$installedIcon = Join-Path $installRoot "TwinQuota.$iconHash.ico"
Copy-Item -LiteralPath $sourceIcon -Destination $installedIcon -Force

if (-not $SkipRegistration) {
    $startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    $shortcutPath = Join-Path $startMenuDirectory "TwinQuota.lnk"
    New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
    if (Test-Path -LiteralPath $shortcutPath) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedExecutable
    $shortcut.WorkingDirectory = $installRoot
    $shortcut.IconLocation = "$installedIcon,0"
    $shortcut.Description = "TwinQuota Antigravity quota monitor"
    $shortcut.Save()

    $uninstallScript = Join-Path $installRoot "uninstall.ps1"
    $uninstallCommand = "powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File `"$uninstallScript`""
    $uninstallKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\TwinQuota"
    $estimatedSize = [Math]::Ceiling((Get-ChildItem -LiteralPath $installRoot -File -Recurse | Measure-Object Length -Sum).Sum / 1KB)
    New-Item -Path $uninstallKey -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name DisplayName -Value "TwinQuota" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name DisplayVersion -Value "1.0.0" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name Publisher -Value "myagmb28Dev" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name InstallLocation -Value $installRoot -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name DisplayIcon -Value "$installedIcon,0" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name UninstallString -Value $uninstallCommand -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name QuietUninstallString -Value "$uninstallCommand -Quiet" -PropertyType String -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name EstimatedSize -Value ([int]$estimatedSize) -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name NoModify -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name NoRepair -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path $uninstallKey -Name InstallDate -Value (Get-Date -Format "yyyyMMdd") -PropertyType String -Force | Out-Null

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class TwinQuotaShellRefresh
{
    [DllImport("shell32.dll")]
    public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);
}
'@
    [TwinQuotaShellRefresh]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}

Write-Host "Installed: $installedExecutable"
if (-not $NoLaunch) {
    Start-Process -FilePath $installedExecutable
}
