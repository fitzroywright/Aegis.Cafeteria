# FFP Integration — v88

## Read model

Aegis.Cafeteria reads:

- `dbo.Employees`: ID, Code, TRN, NFCID, FirstName, LastName, Photo, DepartmentID, EligibleForLunchTicket, CreditLimit, AvailableBalance;
- `dbo.Departments`: Name;
- `dbo.Holidays`: CalendarDate;
- `dbo.LunchItems`: ID, Code, Name, Price, Cost, IsActive;
- `dbo.EmployeeIDScans`: ledger/history/idempotency checks and email reporting.

## Employee identifiers

Universal POS search order:

1. NFC/Card
2. Employee Code
3. TRN
4. Name candidates

Management API uses the integer FFP `Employees.ID` as `CustomerId`.

## Funding policy

Default:

- `CreditLimitLunchItemCode = FFP00`; use `Price`.
- `SubsidyLunchItemCode = FFP00`; use `Cost`.

The policy row(s) are excluded from the saleable catalog.

## FFP ledger write

Each financial projection inserts one `EmployeeIDScans` row if the `TransmissionID` has not already been seen, then updates `Employees.CreditLimit` and `Employees.AvailableBalance` in the same SQL transaction.

Important IDName values include:

- `Tickets Assigned`
- `Cafeteria Purchase`
- `Cafeteria Refund`
- `Additional Credit`
- `Stipend Card Reduction`
- customer-payment/adjustment entries as defined by the POS projection

## Production topology

Recommended: only `Aegis.Cafeteria.Services` has the FFP SQL connection string. POS terminals use `/api/ffp/*` through Services.

See `PRODUCTION-DEPLOYMENT.md` for SQL permissions and Bitwarden configuration.
