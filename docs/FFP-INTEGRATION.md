# FFP Integration — v6

## Read model

Aegis.Cafeteria reads:

- `dbo.Employees`: ID, Code, TRN, NFCID, FirstName, LastName, Photo, DepartmentID, EligibleForLunchTicket, CreditLimit, AvailableBalance;
- `dbo.Departments`: Name;
- `dbo.Holidays`: CalendarDate;
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

## Funding-policy and product boundary

The Services SQLite database is authoritative for the cafeteria funding policy. The `FFP00` row in FFP `dbo.LunchItems` is a compatibility mirror only: `Price` mirrors the Daily Credit Limit Rate and `Cost` mirrors the Daily Company Subsidy Rate. It is never a saleable POS product.

Saleable cafeteria products are owned by each POS SQLite database and are maintained through the POS Administration / Price Maintenance UI. Services and FFP `dbo.LunchItems` are not product-catalog sources.

## v98 product and workforce boundary

Only employees with `FFP.Employees.EligibleForLunchTicket = 1` are active Cafeteria employees. The POS does not load ineligible employees for browse, name search or identifier matching. Historical customer rows may remain locally only to support old transaction history.

Saleable cafeteria products are owned by the POS SQLite database. FFP `dbo.LunchItems` and Cafeteria Services are not product-catalog sources. Product administration changes the current POS catalog only; completed order lines preserve ProductName, UnitPrice and Quantity as immutable transaction snapshots. Reports use those transaction/ledger values rather than looking up today's product price.

`FFP00` is retained solely as a compatibility mirror for the Services-owned funding policy and must not be treated as a saleable product or catalog source.
