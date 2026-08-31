# Aegis.Cafeteria v91

v88 is the production-readiness/documentation release. It retains all v87 POS UI improvements and adds production-safe Bitwarden handling for the POS-to-Services API key, fail-closed production startup, expanded automated regression tests, and a complete production/FFP Manager integration runbook.

## Recommended production architecture

- Windows/Linux POS remains local-first with SQLite.
- POS terminals connect to `Aegis.Cafeteria.Services` over HTTPS.
- Only Services connects directly to the FFP SQL database.
- FFP Manager performs management financial actions through the Windows-authenticated Services management API.
- FFP SQL connection string and service API key are resolved from Bitwarden Secrets Manager.

## Current funding policy

`Monthly Credit Limit = Eligible Working Days × CreditLimitPolicy.Price`

`Monthly Subsidy = min(Monthly Credit Limit, Eligible Working Days × SubsidyPolicy.Cost)`

Both policy codes default to `FFP00` and are independently configurable.

## Key POS capabilities

- universal NFC → Code → TRN → Name customer search;
- retryable employee search inside the results dialog;
- Cash Customer and employee sales;
- Cash, Cafeteria Account and split tender with automatic cafeteria-account reduction as cash is entered;
- live payment balance/change calculation while typing;
- Available to Spend and Payroll Deduction shown to the customer;
- explicit Void Current Sale workflow;
- full/partial/multiple partial refunds with an editable auto-generated factual refund reason;
- Customer Payment reducing Payroll Deduction;
- 50/50 main workspace and aligned grid headers;
- Windows/Linux business-feature parity;
- daily dashboard with Items Sold quantity/value pie charts;
- report center with Preview, XLSX, PDF, CSV and HTML export.

## Management capabilities

Cafeteria Services exposes Windows-authenticated operations for:

- per-employee Additional Credit Limit;
- Approve Additional Credit;
- Stipend/meal advance/reimbursement card reduction;
- manual month opening.

See `docs/FFP-MANAGER-INTEGRATION.md`.

## Production documentation

Start with:

1. `docs/PRODUCTION-DEPLOYMENT.md`
2. `docs/FFP-MANAGER-INTEGRATION.md`
3. `docs/SERVICES.md`
4. `docs/FFP-INTEGRATION.md`
5. `docs/OPERATIONS.md`
6. `docs/TESTING.md`

## Databases

- POS SQLite schema baseline: 73.
- Services SQLite is separate durable management/publication state.
- No new FFP table is required by v88.

## Secrets

Non-Development environments use Bitwarden Secrets Manager through the official `bws` CLI. Production appsettings contain only Bitwarden secret UUIDs, never secret values.

Logical secrets:

- `FfpDatabaseConnectionString`
- `CafeteriaServicesApiKey`
- `MessagingBearerToken` when Messaging is enabled

## Build gate

```powershell
dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
python scripts\static-audit.py
```

The package generated in this environment is source-verified by static audit, but .NET build/test must be run on the .NET build machine before production deployment.


## Coding standard

All C# source uses the Fitzroy/IoT house style documented in `docs/CODING-STANDARDS.md`.
