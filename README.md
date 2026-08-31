# Aegis.Cafeteria v1

This is the reset v1 baseline, derived from the former v100 lineage. Future releases increment from this v1 baseline and use the package name `Aegis.Cafeteria.vN.zip`.

## Recommended production architecture

- Windows/Linux POS remains local-first with SQLite.
- POS terminals connect to `Aegis.Cafeteria.Services` over HTTPS.
- Only Services connects directly to the FFP SQL database.
- FFP Manager performs management financial actions through the Windows-authenticated Services management API.
- FFP SQL connection string and service API key are resolved from Bitwarden Secrets Manager.

## Current funding policy

`Monthly Credit Limit = Eligible Working Days × Daily Credit Limit Rate`

`Monthly Subsidy = min(Monthly Credit Limit, Eligible Working Days × Daily Company Subsidy Rate)`

These rates are stored in `Aegis.Cafeteria.Services` and no longer come from the `FFP00` pseudo-product in `FFP.LunchItems`. Existing monthly allocations retain their snapshotted rates.

## Key POS capabilities

- universal NFC → Code → TRN → Name customer search;
- retryable employee search inside the results dialog;
- Cash Customer and employee sales;
- Cash, Cafeteria Account and split tender with automatic cafeteria-account reduction as cash is entered;
- live payment balance/change calculation while typing;
- Available to Spend and Employee Amount Due shown to the customer;
- explicit Void Current Sale workflow;
- full/partial/multiple partial refunds with an editable auto-generated factual refund reason;
- Customer Payment reducing Employee Amount Due;
- 50/50 main workspace and aligned grid headers;
- Windows/Linux business-feature parity;
- daily dashboard with Items Sold quantity/value pie charts;
- report center with Preview, XLSX, PDF, CSV and HTML export.


## Financial ownership boundary

Aegis.Cafeteria calculates and displays **Employee Amount Due**. It does not create payroll deduction records, track payroll collections, reconcile payroll results, or derive debt from payroll outcomes. Those responsibilities belong to FFP Manager and/or Payroll.

## Management capabilities

Cafeteria Services exposes Windows-authenticated operations for:

- per-employee Additional Credit Limit;
- Approve Additional Credit;
- Stipend/meal advance/reimbursement card reduction;
- manual month opening.
- funding-policy maintenance through FFP Manager/Services.

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
- No new FFP table is required.

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


### Management boundary

FFP Manager owns funding-policy maintenance. Services stores the authoritative audited policy and mirrors the rates into the FFP00 compatibility row. POS Administration is limited to saleable product/price maintenance.

## Workforce/product ownership

- Only FFP employees with `EligibleForLunchTicket = 1` are synchronized into the active POS employee set.
- Saleable products and current prices are owned by the POS SQLite database and maintained from POS Administration.
- FFP `LunchItems` is not a product source. The only retained use is the `FFP00` compatibility mirror for the Services-owned funding policy.
- Completed order lines snapshot product description, unit price and quantity; historical reports never recalculate old sales from the current product table.
- FFP ledger descriptions include transaction-time unit prices for audit/report reconstruction.

## Visual Studio development start

Visual Studio supports the checked-in `Aegis.Cafeteria.slnLaunch` solution launch profiles. Select **Development - Services + Windows POS** in the Visual Studio launch-profile dropdown and press **F5**. Visual Studio starts `Aegis.Cafeteria.Services` followed by the Windows POS (`Aegis.Cafeteria.UI`). Both projects retain their project-level Development launch profiles and therefore run with `DOTNET_ENVIRONMENT=Development`.

For focused debugging, the solution also includes **Development - Services Only** and **Development - Windows POS Only**.

## Development Swagger

When Visual Studio starts `Aegis.Cafeteria.Services` with the `Cafeteria Services Development` profile, it opens `http://localhost:5088/swagger` automatically. Swagger/OpenAPI is registered and mapped only when `DOTNET_ENVIRONMENT=Development`; it is not exposed by the production service.

## Recalculate an opened month (v2)

An already-opened month can be recalculated only while its original Month Opening balances are still untouched. This is intended for correcting the funding policy or holiday calendar before cafeteria financial activity begins.

Management endpoint:

`POST /api/month-opening/recalculate?year=2026&month=8`

From an elevated PowerShell session using the current Windows identity:

```powershell
Invoke-RestMethod `
    -Method Post `
    -Uri "http://localhost:5088/api/month-opening/recalculate?year=2026&month=8" `
    -UseDefaultCredentials
```

The operation is refused if the month has purchases, refunds, customer payments, Additional Credit, stipend card reductions, a changed eligible-employee set, or FFP balances that no longer equal the latest Month Opening allocation. The original allocation is retained for audit; the replacement event becomes authoritative for POS and payroll reporting.

## Development reset

Development exposes `POST /api/development/reset` for a coordinated clean test reset. It is Windows-authenticated and is not available outside `DOTNET_ENVIRONMENT=Development`.

The reset preserves the POS product catalog and current funding policy, but clears Cafeteria transaction/allocation/reference state and Aegis-created FFP test ledger/projection data so Month Opening can be tested again from a clean state.

## v4 Development Reset Fix

v4 fixes the Development reset SQL transaction so the reset result reader is disposed before the FFP transaction commits. This resolves the SQL Server error `There is already an open DataReader associated with this Connection which must be closed first.` Reset behavior and safeguards are otherwise unchanged.

## v5
- Fixed normal application shutdown raising TaskCanceledException from CafeteriaHeartbeatWorker.
- Heartbeat delay now participates in the worker cancellation handler, so stopping the host exits the background service cleanly.
