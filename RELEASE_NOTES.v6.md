# Aegis.Cafeteria v6

## Added
- EF Core persistence layer.
- Local-first SQLite operational database.
- Configurable SQL Server or PostgreSQL central provider.
- Atomic local transactional outbox.
- Central inbox idempotency.
- Monotonic central change journal and pull cursor.
- Automatic connectivity probing, network-change wakeup, exponential offline retry and automatic resynchronization.
- Interrupted-outbox recovery, poison-message state and succeeded-outbox retention cleanup.
- Application persistence abstraction accepting domain objects.
- Structured Serilog logging to Graylog `10.10.0.3:12201` plus rolling local log fallback.
- Sanitized business-event audit logging.
- Persistence/synchronization test project and updated documentation package.

## Deployment note
Configure a Graylog GELF UDP input on port 12201 (or change `CafeteriaLoggingOptions`).

## Test execution note
This generated package includes the full automated test source and test scripts. The generation environment had no .NET SDK and no outbound DNS to install it, so `dotnet restore/build/test` could not be executed here. Run `scripts/test-all.ps1` or `scripts/test-all.sh` on a .NET 10 development/CI machine before deployment.
