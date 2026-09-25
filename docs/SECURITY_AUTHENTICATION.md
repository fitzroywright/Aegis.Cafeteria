# Security and Authentication Guide

## Principles

- Do not store SQL passwords in Aegis.Cafeteria.
- Do not require Bitwarden for the normal Cafeteria runtime path.
- Do not commit GitHub package credentials, Active Directory passwords, management service tokens, bearer tokens, or SMTP credentials.
- Prefer trusted service identity and Common.Security over application-held secrets.

## Common.Security

Common.Security is restored from the private GitHub Packages feed configured in `NuGet.config` and the source repository is `fitzroywright/Common.Security`.

### GitHub Actions

The workflow grants `contents: read` and `packages: read`. It first looks for a repository Actions secret named `COMMON_SECURITY_TOKEN`. If that secret exists, it is used both to verify checkout access to `fitzroywright/Common.Security` and to authenticate the `github-common` NuGet feed. If the dedicated secret is absent, the workflow falls back to the current repository `GITHUB_TOKEN`.

A plain `GITHUB_TOKEN` only works when the Common.Security repository/package explicitly grants Aegis.Cafeteria Actions access. If GitHub Packages returns HTTP 403, either grant Aegis.Cafeteria access to the Common.Security package/repository or configure `COMMON_SECURITY_TOKEN`.

The dedicated token must have the minimum permissions needed to read the private `fitzroywright/Common.Security` repository and read its GitHub Packages NuGet package. For a classic PAT this normally means private repository read access plus `read:packages`. Never commit the token.

### Developer workstations

A developer performing a fresh clone needs a GitHub credential that can read the private Common.Security package feed. Store that credential only in local NuGet configuration or an environment variable; never in committed `NuGet.config`.

Set `GITHUB_PACKAGES_TOKEN` locally, then run:

```powershell
./scripts/configure-github-packages.ps1
```

The helper configures the `github-common` source and immediately performs a restore, so a bad token fails before development begins.

## Management authentication

Management is now hosted directly by Cafeteria Services under `/management/`; there is no separate Aegis.Cafeteria.Management process. Credentials are submitted to Cafeteria Services for Common.Security authentication. Services stores a short-lived management session and returns only an HttpOnly SameSite management cookie. Treat the underlying service token as sensitive and never log it.

## SQL

Cafeteria Services uses Windows Integrated Security to FFP SQL. The production model intentionally avoids SQL credentials. The intended transport is TCP with Encrypt=False for the legacy/trusted FFP environment unless explicitly changed in a later release.

## Audit

Do not record passwords, package tokens, management tokens, connection strings, or raw authentication request bodies in AuditTrail or Graylog. Audit business actions and identity/context, not secrets.

## Graylog

GELF UDP transport is best-effort and must not block business transactions. Network ACLs should permit the application hosts to send UDP 1699 only to the approved Graylog host where practical.
