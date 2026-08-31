# Database Deployment — v88

## POS SQLite

Current POS schema baseline: **73**.

Windows default:

`C:\ProgramData\Aegis.Cafeteria\cafeteria.local.db`

Linux default:

`/var/lib/aegis-cafeteria/cafeteria.local.db`

For a new production terminal, deploy with no existing database and allow startup initialization to create it. Never seed production by copying a developer database.

The POS database contains operational/audit state and must be backed up. It is not a disposable cache.

## Services SQLite

Recommended path:

`C:\ProgramData\Aegis.Cafeteria.Services\cafeteria.services.db`

It stores:

- durable management service events;
- FFP publication attempts/status;
- per-employee `CustomerCreditPolicies.AdditionalCreditLimit`;
- event sequencing used by POS synchronization.

This database must also be backed up.

## FFP database

No new FFP table is required by v88. The integration relies on existing FFP tables/columns documented in `FFP-INTEGRATION.md`.

Production least-privilege SQL grants are documented in `PRODUCTION-DEPLOYMENT.md`.
