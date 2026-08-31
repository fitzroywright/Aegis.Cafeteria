# Aegis.Cafeteria v88 Release Notes

## Production readiness

- Rewrote and updated the project documentation for the current architecture and feature set.
- Added `docs/PRODUCTION-DEPLOYMENT.md` with end-to-end Windows/Linux POS, Services, AD, SQL, HTTPS, Bitwarden, month-opening and go-live instructions.
- Added `docs/FFP-MANAGER-INTEGRATION.md` defining the required FFP Manager actions, API contracts, Windows authentication, idempotency and acceptance tests.
- Added production configuration examples under `deployment/examples/`.

## Secrets hardening

- Added logical secret `CafeteriaServicesApiKey`.
- POS Services mode now resolves its API key through `IApplicationSecretProvider` when `CafeteriaServices:ApiKey` is blank.
- Aegis.Cafeteria.Services resolves the same key for `/api/pos/*` and `/api/ffp/*` protection.
- Production appsettings no longer contain the previously embedded FFP SQL connection string.
- Windows POS, Linux POS and Services fail closed outside Development when Services/API-key protection is required but the key is missing.

## Tests

Added/expanded regression coverage for:

- Bitwarden-backed FFP connection string resolution;
- Bitwarden-backed Cafeteria Services API key resolution;
- Services mode avoiding direct POS retrieval of the FFP SQL secret;
- configured Development API-key override;
- XLSX Open XML package generation;
- PDF generation header/title/page output;
- Additional Credit exact-cap approval and zero/negative rejection;
- static production configuration/documentation requirements.

## No database schema change

- POS SQLite schema remains 73.
- Services storage model is unchanged from the current service-event/credit-policy schema.
- No new FFP table is required.
