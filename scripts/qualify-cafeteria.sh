#!/usr/bin/env bash
set -euo pipefail
if [ "$#" -ne 1 ]; then
  echo 'Usage: qualify-cafeteria.sh /absolute/acceptance-manifest.json' >&2
  exit 2
fi
repo="$(cd "$(dirname "$0")/.." && pwd)"
dotnet restore "$repo/Aegis.Cafeteria.Services/Aegis.Cafeteria.Services.csproj" -m:1 -p:BuildInParallel=false
dotnet build "$repo/Aegis.Cafeteria.Services/Aegis.Cafeteria.Services.csproj" --configuration Release --no-restore -m:1 -p:BuildInParallel=false
for project in UnitTests PersistenceTests ReportingTests; do
  dotnet test "$repo/Aegis.Cafeteria.Tests/Aegis.Cafeteria.$project/Aegis.Cafeteria.$project.csproj" --configuration Release -m:1 -p:BuildInParallel=false
done
export CAFETERIA_ACCEPTANCE_REASON=ReleaseQualification
dotnet "$repo/Aegis.Cafeteria.Services/bin/Release/net10.0/Aegis.Cafeteria.Services.dll" --cafeteria-acceptance "$1"
