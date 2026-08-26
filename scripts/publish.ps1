[CmdletBinding()]
param(
    [string]$Runtime = "win-x64",
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts\$Runtime"))
$publishRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "app"))
$archivePath = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "TwinQuota-$Runtime.zip"))

if (-not $publishRoot.StartsWith($artifactsRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean a publish directory outside the runtime artifacts folder."
}

if (Test-Path -LiteralPath $publishRoot) {
    Remove-Item -LiteralPath $publishRoot -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

$publishArguments = @(
    "publish",
    (Join-Path $repositoryRoot "src\TwinQuota.Windows\TwinQuota.Windows.csproj"),
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--self-contained", "false",
    "-p:NuGetAudit=false",
    "-p:PublishSingleFile=true",
    "-p:DebugType=None",
    "--output", $publishRoot
)
if ($NoRestore) {
    $publishArguments += "--no-restore"
}

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$hookPublishArguments = @(
    "publish",
    (Join-Path $repositoryRoot "src\TwinQuota.Hook\TwinQuota.Hook.csproj"),
    "--configuration", "Release",
    "--runtime", $Runtime,
    "--self-contained", "false",
    "-p:NuGetAudit=false",
    "-p:PublishSingleFile=true",
    "-p:DebugType=None",
    "--output", $publishRoot
)
if ($NoRestore) {
    $hookPublishArguments += "--no-restore"
}

& dotnet @hookPublishArguments
if ($LASTEXITCODE -ne 0) {
    throw "TwinQuota hook publish failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $publishRoot)) {
    throw "Publish output was not created at $publishRoot."
}

Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $archivePath
Write-Host "Package: $archivePath"
