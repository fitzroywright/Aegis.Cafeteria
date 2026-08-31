# Testing — v88

## Automated test suites

- `Aegis.Cafeteria.UnitTests` — domain/application rules.
- `Aegis.Cafeteria.PersistenceTests` — SQLite, FFP adapter/reference cache, secret configuration and infrastructure behavior.
- `Aegis.Cafeteria.ReportingTests` — report catalog and exporters.
- `scripts/static-audit.py` — cross-platform/UI/configuration regression contracts that are difficult to exercise headlessly.

## v88 additions

- direct FFP mode resolves `FfpDatabaseConnectionString` from the secret provider when plaintext configuration is blank;
- Services mode resolves `CafeteriaServicesApiKey` from the secret provider and does not request the FFP DB secret on a POS;
- explicit configured Services API key overrides secret lookup for Development/testing scenarios;
- Excel exporter produces a valid Open XML ZIP package with workbook/worksheet parts;
- PDF exporter produces a PDF header/title/page content;
- Additional Credit allows cumulative approval exactly to the employee cap and rejects zero/negative approvals;
- static audit requires production appsettings to contain no plaintext FFP connection string and verifies Bitwarden API-key wiring/fail-closed startup.

## Core financial regression areas

- monthly Credit Limit and Subsidy calculation;
- employee eligibility and working days;
- Additional Credit caps/idempotency;
- stipend split between cash and card reduction;
- purchases, split tender, Customer Responsibility and Employee Amount Due;
- Customer Payment;
- carried debt;
- full/partial refunds;
- void vs ESC semantics;
- Cash Customer isolation;
- FFP outbox/idempotency;
- management event synchronization;
- report zero-row suppression and export consistency.

## Build gate

```powershell
dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
python scripts\static-audit.py
```

## Mandatory pre-production smoke tests

1. Windows POS sign-in and LDAPS authentication.
2. Linux POS sign-in and feature-parity spot check.
3. NFC/Code/TRN/Name lookup including retryable in-dialog search.
4. Cash sale.
5. Cafeteria Account sale showing live Employee Amount Due effect.
6. Split tender with live remaining/change update while typing.
7. Void Current Sale requiring manager reason.
8. Partial refund and multiple partial refunds.
9. Customer Payment reducing Employee Amount Due.
10. second customer display: name/photo, Available to Spend, Employee Amount Due, current-month activity.
11. Services outage during sale followed by recovery/outbox publication.
12. FFP Manager Additional Credit within/above cap.
13. Stipend.
14. manual month opening and idempotent rerun.
15. reports Preview, XLSX and PDF on a large date range; UI remains responsive.


## v91 regression coverage

Automated tests cover split-tender auto-allocation and generated refund reasons. Manual smoke testing should verify:

1. With enough account capacity, entering $300 cash on a $1,000 sale changes Cafeteria Account to $700 immediately.
2. With only $600 account capacity, entering less than the required $400 cash does not reduce the $600 account amount and Remaining still shows the shortage.
3. Cash covering the full order reduces Cafeteria Account to zero and any excess appears as Change Due.
4. Windows and Linux refund reason dialogs open pre-populated with selected item quantities/product names and original sale time, and the text remains editable.

## v92 laptop Services/POS integration test

Use `docs/LAPTOP-INTEGRATION-TEST.md` and the scripts under `scripts/windows` to run Cafeteria Services and the Windows POS together on one laptop. The Development profile uses isolated fake FFP reference/accounting data and never writes to production FFP SQL.

Required regression scenarios are:

1. SignalR causes an immediate durable pull after Additional Credit approval.
2. Disabling SignalR still delivers the event through the 15-second poll.
3. Stopping the POS, creating events, and restarting the POS catches up from the last processed sequence.
4. If the affected employee is displayed, the cashier and customer-facing screens refresh automatically.
5. Reusing an operation ID does not apply Additional Credit twice.

## v95 financial-boundary tests

Verify that Windows/Linux display **Employee Amount Due**, customer payments reduce it, and no POS/service workflow creates payroll deduction records, payroll collection results or payroll-derived carry-forward debt. Any prior-period balance entering Cafeteria must come from an external management integration or explicit prior-period adjustment.

## v97 real-FFP development profile

The default Development Services profile uses `JMDB01/FFP` as the controlled integration-test database with `FfpIntegration:AllowWrites=true`. Development therefore exercises both FFP reads and writes. `MonthOpening:Enabled` remains false so starting the laptop test environment does not automatically bulk-allocate the test population; run Month Opening manually when that scenario is under test.
