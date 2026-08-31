$ErrorActionPreference = 'Stop'
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --collect:"XPlat Code Coverage"
