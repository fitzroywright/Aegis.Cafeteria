param(
    [string]$Token = $env:GITHUB_PACKAGES_TOKEN,
    [string]$UserName = "fitzroywright"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    throw "Set GITHUB_PACKAGES_TOKEN to a GitHub credential that can read the Common.Security package feed, or pass -Token explicitly."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$configFile = Join-Path $repoRoot "NuGet.config"

Write-Host "Configuring github-common NuGet source for Common.Security..."
dotnet nuget update source github-common `
    --username $UserName `
    --password $Token `
    --store-password-in-clear-text `
    --configfile $configFile

if ($LASTEXITCODE -ne 0) {
    throw "Failed to configure the github-common NuGet source."
}

Write-Host "Validating package restore..."
dotnet restore $repoRoot --configfile $configFile

if ($LASTEXITCODE -ne 0) {
    throw "NuGet authentication was configured, but restore failed. Verify that the token can read the private Common.Security package."
}

Write-Host "Common.Security package access is configured and restore succeeded."
