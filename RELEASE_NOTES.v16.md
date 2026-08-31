# Aegis.Cafeteria v16

## Application logging dependency fix

- Added an explicit `Microsoft.Extensions.Logging.Abstractions` 10.0.0 package reference to `Aegis.Cafeteria.Application`.
- The Application layer uses `ILogger<T>` in `AdditionalCreditAdministrationService`; it no longer relies on a transitive logging reference from Infrastructure/UI/TestHost.
- The full `Microsoft.Extensions.Logging` implementation package is intentionally not referenced by Application. Logging providers/configuration remain composition-root concerns.
- Includes all fixes through v15, including Bitwarden Secrets Manager, Serilog removal, UI bootstrap fixes, SQLite test helper, and SQLite `DateTimeOffset` client-side ordering fixes.
