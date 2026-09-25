# FFP Cafeteria Platform

This repository contains the FFP cafeteria point-of-sale, management service and integration components used to operate staff cafeteria transactions from counter sale through reporting and FFP ledger publication.

## What is implemented

- Windows and Linux point-of-sale clients with local-first operation.
- Universal customer search using NFC tag, employee code, TRN and name.
- Employee cafeteria-account sales, cash-customer sales and split tender.
- Live payment balance, change calculation, Available to Spend and Employee Amount Due display.
- Explicit void, full refund, partial refund, multiple partial refund and customer payment workflows.
- Monthly funding policy based on eligible working days, daily credit-limit rate and daily company-subsidy rate.
- Additional credit approval and stipend/card-reduction business operations remain service-owned; their temporary web UI is retained only until the FFP Manager client is implemented.
- POS Administration owns price maintenance, employee email preferences, automatic-operation overrides, report/email settings, Employee Amount Due preview/resend, operational audit and diagnostics access.
- Daily dashboard, close-out, itemized, employee, department and period reporting.
- Preview, spreadsheet, PDF, CSV and HTML report exports.
- Service-side integration with FFP employee, holiday, ledger and balance projection data.
- Audit trail for protected and mutating operations.
- Development Swagger, health endpoint, development reset and backup/restore support.

## Financial policy

Monthly Credit Limit = Eligible Working Days × Daily Credit Limit Rate

Monthly Subsidy = the lower of Monthly Credit Limit and Eligible Working Days × Daily Company Subsidy Rate

Unused positive credit does not carry forward. Unpaid employee responsibility carries forward. Refunds and customer payments are recorded as explicit events so historical reporting remains traceable.

## Operating boundary

The POS clients operate locally and use their local data store for cashier work. The service layer owns central integration, management operations, audit records, event publication and direct FFP database access. FFP Manager calls the service layer for cafeteria management actions instead of directly editing cafeteria financial state.

## Documentation index

- `docs/ARCHITECTURE.md` - runtime architecture and data ownership.
- `docs/OPERATIONS.md` - cashier, management and support procedures.
- `docs/PRODUCTION-DEPLOYMENT.md` - production topology and configuration runbook.
- `docs/SERVICES.md` - service-layer responsibilities and endpoints.
- `docs/FFP-INTEGRATION.md` - FFP data integration model.
- `docs/FFP-MANAGER-INTEGRATION.md` - manager-side operating boundary.
- `docs/REPORTING.md` - report inventory and export behavior.
- `docs/TESTING.md` - acceptance and verification coverage.
- `docs/MANAGEMENT-DEMO.md` - management demo narrative.
- `docs/IT-DEMO.md` - IT demo narrative and technical follow-up items.

## Build gate

```powershell
dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
python scripts\static-audit.py
```

Run the build gate on the .NET build machine before production deployment.


## One-time product bootstrap

Product catalog bootstrap is a deployment/cutover operation, not a permanent management screen. It is available through the migration CLI only while `Migration:Enabled` is true on Cafeteria Services.

Dry run:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- --product-bootstrap --services-url https://cafeteria-services
```

Execute after reviewing the dry-run counts:

```powershell
dotnet run --project Aegis.Cafeteria.Migration -- --product-bootstrap --services-url https://cafeteria-services --execute
```

The bootstrap is idempotent, ignores configured funding-policy product codes, and does not overwrite products that already have import events.
