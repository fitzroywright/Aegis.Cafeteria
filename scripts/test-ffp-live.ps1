param(
    [string]$ConnectionString = ""
)

$ErrorActionPreference = 'Stop'
$env:AEGIS_FFP_LIVE_TEST = '1'
if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $env:AEGIS_FFP_TEST_CONNECTION_STRING = $ConnectionString
}

Write-Host "Running live FFP integration test..."
Write-Host "Default connection: JMDB01 / FFP using Windows Integrated Security"
dotnet test --filter "FullyQualifiedName~FfpSqlLiveIntegrationTests"
