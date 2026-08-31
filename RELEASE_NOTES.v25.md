# Aegis.Cafeteria v25

## Fixes

- Fixed C# compiler error in `CsvReportExporter` caused by a public `Format` property and private `Format(...)` method sharing the same member name.
- Renamed the private decimal formatter to `FormatDecimal(...)` and updated all call sites.
- Scanned the Reporting project for similar property/method name collisions; none were found.
- Preserves all v24 customer UI, v23 FFP integration, v22 reporting, Graylog, Zabbix heartbeat, weekly messaging, durable FFP publication, and prior SQLite/outbox fixes.

## Verification

- Static source scan passed.
- ZIP integrity verified.
- `dotnet build` / `dotnet test` could not be run in the generation environment because the .NET SDK is unavailable.
