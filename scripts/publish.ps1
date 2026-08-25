[CmdletBinding()]
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repositoryRoot "artifacts\$Runtime\app"
$archivePath = Join-Path $repositoryRoot "artifacts\$Runtime\TwinQuota-$Runtime.zip"

dotnet publish (Join-Path $repositoryRoot "src\TwinQuota.Windows\TwinQuota.Windows.csproj") `
    --configuration Release `
    --runtime $Runtime `
    --self-contained false `
    -p:NuGetAudit=false `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    --output $publishRoot

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

Compress-Archive -Path (Join-Path $publishRoot "*") -DestinationPath $archivePath
Write-Host "Package: $archivePath"
