[CmdletBinding()]
param(
    [string]$Config = "src\Aegis.Cafeteria.UI\appsettings.json"
)
$ErrorActionPreference = "Stop"

Write-Host "Cafeteria v47 Windows prerequisites" -ForegroundColor Cyan

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) { throw ".NET SDK was not found. Install .NET 10 SDK." }
$version = (& dotnet --version).Trim()
if (-not $version.StartsWith("10.")) { throw "A .NET 10 SDK is required. Found $version." }
Write-Host "[OK] .NET SDK $version"

if (-not (Test-Path $Config)) { throw "Configuration file not found: $Config" }
$configJson = Get-Content $Config -Raw | ConvertFrom-Json

if ($configJson.FfpIntegration.Enabled) {
    $ffpConnection = [string]$configJson.FfpIntegration.ConnectionString
    $ffpSecretId = [string]$configJson.SecretsManager.SecretIds.FfpDatabaseConnectionString
    if ([string]::IsNullOrWhiteSpace($ffpConnection) -and [string]::IsNullOrWhiteSpace($ffpSecretId)) {
        throw "FFP integration is enabled but neither FfpIntegration:ConnectionString nor SecretsManager:SecretIds:FfpDatabaseConnectionString is configured."
    }
    Write-Host "[OK] FFP integration has a configured connection source"
}

$secretIds = @()
if ($null -ne $configJson.SecretsManager -and $null -ne $configJson.SecretsManager.SecretIds) {
    foreach ($property in $configJson.SecretsManager.SecretIds.PSObject.Properties) {
        if (-not [string]::IsNullOrWhiteSpace([string]$property.Value)) { $secretIds += [string]$property.Value }
    }
}

if ($secretIds.Count -gt 0) {
    $bws = Get-Command bws -ErrorAction SilentlyContinue
    if (-not $bws) { throw "One or more Bitwarden secret IDs are configured but bws.exe was not found on PATH." }
    if ([string]::IsNullOrWhiteSpace($env:BWS_ACCESS_TOKEN)) { throw "BWS_ACCESS_TOKEN is required because Bitwarden secret IDs are configured." }
    Write-Host "[OK] Bitwarden Secrets Manager CLI and access token are available"
}
else {
    Write-Host "[OK] No Bitwarden secret IDs are required by the current configuration"
}

Write-Host "[INFO] Verify the workstation trusts the FFTPJ CA and can reach JMAD01/JMAD02 on LDAPS 636 before operator-login testing."
Write-Host "Prerequisite check passed." -ForegroundColor Green
