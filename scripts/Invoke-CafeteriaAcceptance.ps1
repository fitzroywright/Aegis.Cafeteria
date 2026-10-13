param(
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [string]$ServicesAssembly,
    [string]$ExpectedRunnerSha256,
    [ValidateSet('OnDemand', 'AfterDeployment', 'ReleaseQualification')][string]$Purpose = 'OnDemand',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifest = (Resolve-Path $ManifestPath).Path
if (-not $env:CAFETERIA_ACCEPTANCE_FFP) {
    Write-Warning 'Dedicated acceptance credentials are absent. The runner will record blocked evidence.'
}
if (-not $ServicesAssembly) {
    $project = Join-Path $repo 'Aegis.Cafeteria.Services/Aegis.Cafeteria.Services.csproj'
    if (-not $SkipBuild) {
        & dotnet restore $project
        if ($LASTEXITCODE -ne 0) { throw 'Cafeteria restore failed.' }
        & dotnet build $project --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'Cafeteria Release build failed.' }
    }
    $ServicesAssembly = Join-Path $repo 'Aegis.Cafeteria.Services/bin/Release/net10.0/Aegis.Cafeteria.Services.dll'
}
if ($SkipBuild -or $PSBoundParameters.ContainsKey('ServicesAssembly')) {
    if (-not $ExpectedRunnerSha256) { throw 'A reviewed acceptance runner SHA256 is required when using a prebuilt assembly.' }
    if ((Get-FileHash $ServicesAssembly -Algorithm SHA256).Hash -ne $ExpectedRunnerSha256) { throw 'Acceptance runner SHA256 does not match.' }
}
$runnerBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $ServicesAssembly).Path)
if (-not [System.Text.Encoding]::UTF8.GetString($runnerBytes).Contains('CafeteriaAcceptanceCommand')) {
    throw 'This assembly does not contain the isolated acceptance command. It must not be started with acceptance arguments.'
}
$env:CAFETERIA_ACCEPTANCE_REQUESTED_BY = [Environment]::UserName
$env:CAFETERIA_ACCEPTANCE_REASON = $Purpose
& dotnet (Resolve-Path $ServicesAssembly).Path --cafeteria-acceptance $manifest
exit $LASTEXITCODE
