# Aegis.Cafeteria v24

## Customer-facing POS UI refresh

- Rebuilt the Windows POS main screen into a polished two-pane cashier workspace.
- Added a clear idle state for NFC/barcode scanning and manual employee search.
- Added a customer directory browser backed entirely by the local FFP employee cache.
- Customer browser supports blank alphabetical browse plus name, TRN, NFC and barcode search.
- Customer browser displays employee photo, Employee ID, TRN, NFC, barcode and cafeteria-credit eligibility.
- Selected customer card displays photo, employee number, eligibility, identifiers, account period, eligible working days, monthly credit limit, subsidy/non-subsidy remaining, total available, carried debt, customer responsibility and period transactions.
- Ineligible employees remain identifiable but cafeteria-account tender capacity is forced to zero; cash tender remains available.
- Customer identity details are read from the existing CustomerIdentifiers cache. No SQLite schema migration is required for v24.
- CustomerAccountView and CustomerSearchCandidate were extended with optional identity fields in a backward-compatible way.
- Empty SearchAsync now intentionally acts as an alphabetical employee-directory browse.

## Preserved fixes

v24 carries forward the v23 FFP Employees/Holidays integration, weekday/holiday allocation rules, v22 reporting, Graylog logging, Zabbix heartbeat, durable FFP publication, weekly messaging, Development secret provider, automatic runtime directory creation, SQLite DateTimeOffset materialization fixes and OperationId outbox ordering.

## Verification

Static source, project/XML and ZIP-integrity checks were run in the generation environment. The environment does not contain the .NET SDK, so `dotnet build` and `dotnet test` were not executed here.
