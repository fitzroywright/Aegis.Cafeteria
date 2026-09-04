# Security and Authentication Guide

## Principles

- Do not store SQL passwords in Aegis.Cafeteria.
- Do not require Bitwarden for the normal Cafeteria runtime path.
- Do not commit GitHub package credentials, Active Directory passwords, management service tokens, bearer tokens, or SMTP credentials.
- Prefer trusted service identity and Common.Security over application-held secrets.

## Common.Security

Common.Security is restored from the GitHub Packages feed configured in `NuGet.config`.

### GitHub Actions

The build workflow grants `packages: read` and authenticates the `github-common` NuGet source using the workflow `GITHUB_TOKEN`. No manually-created package token should be committed to the repository.

### Developer workstations

A developer performing a fresh clone needs a GitHub credential that can read the private Common.Security package feed. Use a GitHub token/credential with the minimum package-read and repository access required by the owning account/organization. Store that credential only in the local NuGet credential configuration or an environment variable; never in `NuGet.config` committed to source.

Use `scripts/configure-github-packages.ps1` to configure the local source from the `GITHUB_PACKAGES_TOKEN` environment variable.

## Management authentication

Management credentials are submitted to Cafeteria Services for Common.Security authentication. Management receives a short-lived service token for authorized proxy calls. Treat that token as sensitive and never log it.

## SQL

Cafeteria Services uses Windows Integrated Security to FFP SQL. The production model intentionally avoids SQL credentials. The intended transport is TCP with Encrypt=False for the legacy/trusted FFP environment unless explicitly changed in a later release.

## Audit

Do not record passwords, package tokens, management tokens, connection strings, or raw authentication request bodies in AuditTrail or Graylog. Audit business actions and identity/context, not secrets.

## Graylog

GELF UDP transport is best-effort and must not block business transactions. Network ACLs should permit the application hosts to send UDP 1699 only to the approved Graylog host where practical.
