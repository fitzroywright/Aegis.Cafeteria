# Aegis.Cafeteria v20

## Secret provider compile fix

- Reworked `GetRequiredSecretAsync` from a default interface implementation into an `IApplicationSecretProvider` extension method.
- The helper is now callable from variables typed as `IApplicationSecretProvider`, `ConfigurationSecretProvider`, or `BwsSecretsManagerProvider`.
- Fixes the v19 compile error where `ConfigurationSecretProvider` appeared not to contain `GetRequiredSecretAsync`.
- Keeps all v19 behavior: Development uses configuration-backed secrets; non-Development environments require Bitwarden Secrets Manager.
