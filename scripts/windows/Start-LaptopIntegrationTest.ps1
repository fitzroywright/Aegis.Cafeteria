param(
    [switch]$ResetData
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$testData = Join-Path $root ".testdata"
New-Item -ItemType Directory -Path $testData -Force | Out-Null

$serviceDb = Join-Path $testData "cafeteria.services.development.db"
$posDb = Join-Path $testData "cafeteria.pos.development.db"

if ($ResetData)
{
    Remove-Item $serviceDb -ErrorAction SilentlyContinue
    Remove-Item $posDb -ErrorAction SilentlyContinue
}

$serviceConnection = "Data Source=$serviceDb"
$posConnection = "Data Source=$posDb;Cache=Shared"

$serviceCommand = @"
Set-Location '$root'
`$env:DOTNET_ENVIRONMENT='Development'
`$env:ASPNETCORE_ENVIRONMENT='Development'
`$env:ServicePersistence__ConnectionString='$serviceConnection'
`$env:FfpIntegration__ConnectionString='Data Source=JMDB01;Initial Catalog=FFP;Integrated Security=true;Pooling=false;Encrypt=false'
`$env:FfpIntegration__AllowWrites='true'
`$env:DevelopmentData__Enabled='false'
dotnet run --project src/Aegis.Cafeteria.Services/Aegis.Cafeteria.Services.csproj --urls http://localhost:5088
"@

$posCommand = @"
Set-Location '$root'
`$env:DOTNET_ENVIRONMENT='Development'
`$env:Persistence__LocalSqliteConnectionString='$posConnection'
`$env:Persistence__TerminalId='LAPTOP-WINDOWS-TEST'
dotnet run --project src/Aegis.Cafeteria.UI/Aegis.Cafeteria.UI.csproj
"@

Start-Process powershell -ArgumentList "-NoExit", "-Command", $serviceCommand

Write-Host "Waiting for Aegis.Cafeteria.Services..."
for ($attempt = 0; $attempt -lt 30; $attempt++)
{
    try
    {
        Invoke-RestMethod -Uri "http://localhost:5088/health" -TimeoutSec 2 | Out-Null
        break
    }
    catch
    {
        Start-Sleep -Seconds 1
    }
}

Invoke-RestMethod -Uri "http://localhost:5088/health" -TimeoutSec 3 | Format-List
Start-Process powershell -ArgumentList "-NoExit", "-Command", $posCommand

Write-Host ""
Write-Host "Laptop integration test started."
Write-Host "Service DB: $serviceDb"
Write-Host "POS DB:     $posDb"
Write-Host "FFP integration target: JMDB01 / FFP (Windows Integrated Security)"
Write-Host "FFP reads/writes: ENABLED in Development test environment"
Write-Host "Use the JMDB01/FFP test database for full end-to-end integration testing."
