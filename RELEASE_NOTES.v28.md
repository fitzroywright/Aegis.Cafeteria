# Aegis.Cafeteria v28

## Architecture: isolate Cafeteria from FFP

v28 introduces an anti-corruption boundary so cafeteria business logic no longer depends on FFP-specific read/write contracts.

- `IWorkforceReferenceSource` is the source-neutral read boundary for customers, holidays and cafeteria funding policy.
- `IWorkforceReferenceCache` persists those snapshots locally.
- `ICustomerFundingProfileProvider` supplies monthly credit and company subsidy inputs to accounting without exposing the source system.
- `IExternalAccountingGateway`, `IExternalLedgerOutbox` and `IExternalLedgerProjectionService` replace FFP-named application-layer accounting interfaces.
- FFP-specific SQL/table knowledge remains in `Aegis.Cafeteria.Infrastructure.Ffp` only.

## Correct FFP adapter mapping

Canonical FFP connection:

`Data Source=JMDB01;Initial Catalog=FFP;Integrated Security=true;Pooling=false;Encrypt=false`

The current FFP SQL adapter maps:

- `dbo.Employees` -> customer identity, eligibility, identifiers, photo, `CreditLimit`, `AvailableBalance`.
- `dbo.Holidays` -> holiday dates.
- active `dbo.LunchItems` where `Code='FFP00'` -> company subsidy daily rate (`Price`).

`CreditRateQuery` is removed from active configuration. The POS no longer requires a manually populated stipend/subsidy schedule.

## Funding model correction

Monthly accounting now uses source-neutral funding inputs:

- Employee/customer monthly credit limit from the workforce source.
- Company subsidy daily rate from the workforce funding policy.
- Eligible working days from the cafeteria calendar (weekends and holidays excluded).

Company subsidy allocation is capped at the employee's total monthly credit limit. The non-subsidy portion is the remainder of the monthly credit limit.

## Missing reference data behavior

Customer browsing/identification no longer crashes when funding data is not yet available. The application returns a temporary zero-credit view and does **not** persist that temporary zero as the immutable monthly allocation, allowing a later successful refresh to calculate the real allocation.

## Cache/persistence

New additive cache tables:

- `CustomerFundingProfiles`
- `CafeteriaFundingPolicies`

Existing SQLite databases are upgraded with idempotent `CREATE TABLE IF NOT EXISTS` startup logic.

## All-project alignment

- Windows POS refreshes customers, holidays and funding policy before becoming interactive.
- Linux POS uses the same startup behavior.
- TestHost `/health/ffp` validates employees, holidays and the FFP00 funding policy.
- Administration month-opening uses `ICustomerFundingProfileProvider` instead of the old central credit-rate schedule path.
- Reporting reconciliation reads through `IWorkforceReferenceSource` rather than an FFP-specific application gateway.

## Tests

- Updated durable-cache integration tests for the source-neutral reference boundary.
- Added funding-profile persistence assertions.
- Added source-failure/preserve-cache coverage.
- Added monthly allocation tests for source-neutral total-credit + company-subsidy calculation.
- Added missing-funding test proving temporary zero allocations are not persisted.
- Live FFP test now validates customers, holidays and FFP00 subsidy rate.

## Verification

Static audit, project/config parsing, JSON validation, delimiter checks and ZIP integrity checks pass in the generation environment. The environment does not contain the .NET SDK, so `dotnet build` / `dotnet test` were not executed here.
