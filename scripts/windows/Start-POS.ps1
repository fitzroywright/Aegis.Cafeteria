$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
Push-Location $root
try {
    & "$PSScriptRoot\Test-Prerequisites.ps1"
    dotnet run --project .\src\Aegis.Cafeteria.UI\Aegis.Cafeteria.UI.csproj
}
finally { Pop-Location }
