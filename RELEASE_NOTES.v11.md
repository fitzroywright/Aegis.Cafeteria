# Aegis.Cafeteria v11

## Secrets Manager

- Added `IApplicationSecretProvider`.
- Added Bitwarden Secrets Manager adapter using the official `bws` CLI.
- Removed central database connection strings from application JSON configuration.
- `BWS_ACCESS_TOKEN` is the only required bootstrap credential outside Bitwarden.
- Secret UUIDs are non-secret configuration and can be overridden with .NET environment-variable syntax.
- Added support for Bitwarden cloud and self-hosted Secrets Manager through `SecretsManager:ServerUrl`.
- Added optional Bitwarden-backed SignalR bearer token support.
- Secrets are cached in process memory only and are not logged.

## Windows laptop testing

- Added `Aegis.Cafeteria.TestHost` for central database + SignalR testing without the FFP Application.
- Added test seed endpoint and additional-credit endpoint.
- Added Windows prerequisite, build/test, TestHost and POS PowerShell scripts.
- Added Bitwarden adapter/configuration tests.
- Added static audit check preventing a plaintext central connection string from being reintroduced into JSON configuration.

## Existing architecture retained

- Local-first SQLite POS.
- SQL Server/PostgreSQL central provider choice.
- Event-driven synchronization only; no application-data polling.
- Graylog `10.10.0.3:12201` plus local rolling logs.
- Windows WinForms and Linux Avalonia UIs.
