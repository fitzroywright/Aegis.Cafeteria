# Aegis.Cafeteria v17

## Fixes
- Fixed compile error in `EfSynchronizationService`: `OutboxRow` has no `Id` property.
- The deterministic in-memory secondary sort for outbox rows now uses `OperationId`, which is the row's stable key.
- Retains all v16 fixes, including explicit `Microsoft.Extensions.Logging.Abstractions` dependency in Application, SQLite-safe DateTimeOffset ordering, SQLite test database helper, Secrets Manager integration, and Serilog removal.

## Verification
- Static source/project checks passed.
- ZIP integrity verified.
- `dotnet build/test` was not executed in this generation environment.
