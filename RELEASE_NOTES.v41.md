# Aegis.Cafeteria v41

## Production-baseline persistence rebuild

- Replaces v39's additive legacy SQLite repair with a clean versioned local schema.
- New SQLite files are created from the complete current EF model and stamped with `LocalSchemaInfo.SchemaVersion=41`.
- Legacy local databases are rejected without modification/deletion; manual archive/reset instructions are in `docs/DATABASE-DEPLOYMENT.md`.
- Adds a dedicated SQL Server `[cafeteria]` schema and clean baseline deployment script.
- Adds central schema version validation.
- Adds explicit lengths for indexed/operational string fields to avoid SQL Server `nvarchar(max)` index design problems.
- Removes obsolete `AllowCentralEnsureCreatedForDevelopment` configuration.

## Tests

- Adds fresh v41 local schema tests.
- Adds regression proving legacy SQLite is not altered/deleted.
- Adds previous-month history/read-only allocation regression.
- Adds Common.Security operator-adapter unit tests.
- Adds customer-display monitor/previous-calendar-month policy tests.

## Documentation

All current README/docs/security/deployment guidance has been updated to the v41 behavior. Historical release notes remain unchanged as historical records.
