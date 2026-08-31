# Aegis.Cafeteria v19

## Development mode without Bitwarden

- Added `ConfigurationSecretProvider`, reading logical secrets from `DevelopmentSecrets`.
- Added `ApplicationSecretProviderFactory` so all hosts use the same selection rule:
  - `Development` => configuration-backed development secrets.
  - Any other environment => Bitwarden Secrets Manager.
- Windows POS, Linux POS, and TestHost now load `appsettings.{Environment}.json` after base `appsettings.json` and before environment variables.
- Added `appsettings.Development.json` examples to all three hosts.
- Development configuration supports environment-variable overrides such as `DevelopmentSecrets__CentralDatabaseConnectionString`.
- Production behavior is unchanged and still requires Bitwarden Secrets Manager.
- TestHost `/health` now reports the active secret source.
- Added tests for the development configuration secret provider.
- Includes all fixes through v18.
