# Aegis.Cafeteria v44

## Compile-remediation release

v44 fixes build breaks left by the v42/v43 greenfield cleanup without changing the v42 database schema or the simplified architecture.

### Fixed

- Replaced every stale six-argument `Customer` construction in unit tests with the current greenfield constructor:
  `Customer(id, name, photo, isEligibleForCredit, additionalCreditLimit)`.
- Restored the `Aegis.Cafeteria.Application.Reporting` namespace import in infrastructure composition so
  `IWeeklyTransactionSummaryQuery`, `IWeeklySummaryDispatchStore`, and `WeeklyCustomerSummaryService` resolve correctly.
- Added the `Microsoft.Extensions.Http` package required by `IServiceCollection.AddHttpClient(...)`.
- Updated the Monthly Allocations report description to use current `employee-funded` and `company subsidy` terminology.
- Added a constructor-contract regression test so legacy Customer signatures cannot silently return.

### Database

No schema change. Fresh installations still use local SQLite schema version 42.

### Architecture

No central Aegis database is reintroduced. FFP remains the central source/projection system and SQLite remains the complete local POS operational store.
