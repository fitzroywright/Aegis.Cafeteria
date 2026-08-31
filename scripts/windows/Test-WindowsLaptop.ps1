param(
    [ValidateSet('Debug','Release')]
    [string]$Configuration = 'Debug',
    [switch]$SkipBuild,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
Push-Location $root
try {
    Write-Host 'Cafeteria v47 - Windows validation' -ForegroundColor Cyan
    Write-Host 'Architecture: local SQLite + FFP integration; no central Aegis database/TestHost/SignalR.'

    if (-not $SkipBuild) {
        dotnet restore
        dotnet build .\Aegis.Cafeteria.slnx -c $Configuration --no-restore
    }

    if (-not $SkipTests) {
        dotnet test .\Aegis.Cafeteria.slnx -c $Configuration --no-build
    }

    Write-Host ''
    Write-Host 'Automated validation complete.' -ForegroundColor Green
    Write-Host 'For the manual POS test, run scripts\windows\Start-POS.ps1.'
    Write-Host 'With a second monitor connected, identify a customer and verify the customer display opens maximized on that monitor.'
    Write-Host 'Verify FFP reference refresh and FFP ledger publication against a controlled test/restored FFP database before production.'
}
finally {
    Pop-Location
}
