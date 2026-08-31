# Aegis.Cafeteria v65

## Greenfield SQLite funding schema

- Assumes the previous local SQLite database has been deliberately deleted/archived before first start.
- Local schema version is now 65.
- Removes the v64 one-time legacy-allocation correction path.
- Removes `LegacyPolicyMirror` from persistence models.
- Removes the old physical `DailyCreditRate` and `CompanySubsidyDailyRate` column aliases.
- New SQLite tables store `Ffp00Price` directly.
- Monthly credit remains exactly `EligibleWorkingDays * FFP00.Price`.
- Ordinary LunchItems prices do not influence employee funding.

## Development UI project settings

- Keeps `src/Aegis.Cafeteria.UI/appsettings.Development.json` in the Windows UI project/output.
- Adds `src/Aegis.Cafeteria.UI/Properties/launchSettings.json`.
- Visual Studio profile **Cafeteria Development** sets `DOTNET_ENVIRONMENT=Development` automatically for F5 debugging.

No automatic database deletion or migration is performed by the application.
