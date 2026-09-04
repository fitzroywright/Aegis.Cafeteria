# Programmer Guide

## Solution structure

Aegis.Cafeteria is organized into Domain, Application, Infrastructure, Services, Windows UI, Linux UI, Management, Reporting, and test projects. Keep business rules out of UI projects. Prefer one responsibility per class, one class per file, dependency injection, composition over inheritance, small well-named methods, feature-oriented organization, file-scoped namespaces, Allman braces, explicit local types, and no underscore prefix on private fields.

## Main runtime flow

POS -> Aegis.Cafeteria.Services -> FFP SQL / local Services persistence.

The POS must not reintroduce legacy direct FFP SQL access. Cafeteria Services is the integration boundary for FFP data and authoritative management operations.

## Authentication

Management uses Common.Security via Cafeteria Services. Cafeteria Services authenticates and issues the Management Console a short-lived service token. Do not put network passwords, SQL passwords, bearer tokens, package tokens, or secrets in source control.

## FFP SQL

Production FFP SQL access uses Windows Integrated Security. The intended connection model is TCP + trusted connection + Encrypt=False. Keep the SQL host/database in configuration, but do not add SQL credentials.

## Logging and auditing

Application logging uses Microsoft.Extensions.Logging. Graylog transport is GELF UDP. Current Graylog destination: 10.10.0.3:1699.

Business mutations are audited to the Services AuditTrail persistence and emitted to Graylog. Auditing must never mask the original business response. Never audit passwords, connection strings, package credentials, or raw authentication payloads.

## Development workflow

1. Clone the repository.
2. Configure GitHub Packages credentials for Common.Security using `scripts/configure-github-packages.ps1`.
3. Run `dotnet restore --configfile NuGet.config`.
4. Run `dotnet build`.
5. Run `dotnet test`.
6. Make changes only on the active development branch, never on the frozen release baseline.

## Database changes

Services currently uses SQLite for its runtime persistence. Preserve backward compatibility where practical. When adding persisted fields used by older rows, prefer nullable/default-compatible schema changes unless a formal migration is introduced.

## UI behavior

Windows and Linux behavior should remain semantically aligned. Date/time presentation is standardized to `MMM dd yyyy hh:mm tt` for date/time and `MMM dd yyyy` for date-only values. Customer-facing payment rows are presentation views of account movements and should use user-friendly signs/labels rather than exposing internal accounting signs directly.
