# Testing Guide - First Release Candidate

## Automated suites

- `Aegis.Cafeteria.UnitTests` - domain/application rules.
- `Aegis.Cafeteria.PersistenceTests` - SQLite, FFP adapters/reference cache and infrastructure behavior.
- `Aegis.Cafeteria.ReportingTests` - report catalog and exporters.
- `scripts/static-audit.py` - static regression contracts where applicable.

## Standard build gate

```powershell
./scripts/configure-github-packages.ps1
dotnet clean
dotnet restore --configfile NuGet.config
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
```

`Common.Security` is restored from GitHub Packages. CI authenticates with the workflow `GITHUB_TOKEN` and `packages: read`; local developers use a local GitHub Packages credential and must never commit it.

## Release-candidate architecture checks

- POS has no legacy direct FFP SQL fallback.
- POS communicates with Cafeteria Services.
- Services owns FFP SQL access using Windows Integrated Security / Trusted Connection.
- The intended FFP SQL transport is TCP with `Encrypt=False` for the current trusted legacy environment.
- Normal Cafeteria runtime does not depend on Bitwarden.
- Application centralized logging is GELF UDP to `10.10.0.3:1699`; Syslog UDP is not used.
- Business audit events are persisted in the Services AuditTrail table and emitted to Graylog.

## Core financial regression areas

- monthly Credit Limit and Subsidy calculation;
- employee eligibility and working days;
- Additional Credit caps/idempotency;
- stipend split between cash and card reduction;
- purchases and split tender;
- Employee Amount Due / Outstanding Balance;
- payroll deduction excluded from Outstanding Balance;
- Customer Payment reducing outstanding debt;
- carried debt;
- full/partial refunds;
- refund selection showing purchases only, not payments;
- void/cancel/ESC behavior;
- cash-customer isolation;
- FFP publishing/idempotency;
- management event synchronization;
- report zero-row suppression and export consistency.

## Mandatory smoke tests

1. Windows POS sign-in and Common.Security/AD authentication path.
2. Linux POS sign-in and feature-parity spot check.
3. NFC/Code/TRN/Name lookup.
4. Cash sale.
5. Cafeteria Account sale showing live outstanding-balance effect.
6. Split tender with live remaining/change update.
7. ESC/cancel flow.
8. Partial refund and multiple partial refunds.
9. Refund Selected grid contains purchases only.
10. Customer Payment shows Amount Due before payment, blank From Card, Cash Payment received, and new Balance.
11. Customer-facing display shows employee/photo and current activity with customer-friendly payment signs.
12. Services outage/recovery behavior.
13. Management sign-in.
14. Management employee search and result selection loads employee context.
15. Additional Credit within and above cap.
16. Stipend processing.
17. Rates load/save.
18. Manual Month Opening and recalculation.
19. Employee Amount Due report load.
20. Payroll report send and intentional force-resend.
21. Manager weekly report settings load/save.
22. Test Email queues successfully.
23. Audit Trail returns management mutations with actor/status information.
24. Graylog receives GELF UDP on `10.10.0.3:1699`.
25. Date/time output follows `MMM dd yyyy hh:mm tt` where a time is shown.

## Failure-injection checks

Where practical, test Graylog unavailable, SMTP unavailable and FFP SQL temporarily unavailable. GELF transport failure must not fail a sale or replace a business response. Authoritative dependency failures such as unavailable FFP SQL should fail clearly and diagnostically.

## Development integration profile

Development-only data/reset capabilities must remain disabled in production. Any laptop integration profile must be clearly isolated from production data unless an explicit controlled test intentionally targets the FFP integration environment.
