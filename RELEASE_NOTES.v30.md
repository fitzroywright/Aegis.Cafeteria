# Aegis.Cafeteria v30

## Compile fixes

- Fixed `FfpLedgerPublisherWorker` to use the durable FFP outbox row's real `EmployeeId` member instead of stale `CustomerId` references.
- Fixed `WeeklyCustomerSummaryServiceTests` to assert `AegisEmployeeMessage.EmployeeId`; the messaging contract remains explicitly employee-oriented.
- Added static regression scans for the removed member names.

## Funding and POS UI improvements

- Added `CustomerFundingStatusView` to the customer account read model.
- Windows and Linux POS now show whether funding data is authoritative or unavailable.
- The customer card distinguishes:
  - employee monthly credit loaded/missing;
  - company subsidy policy (`FFP00`) loaded/missing;
  - current employee credit amount;
  - current company subsidy daily rate.
- Updated visible labels from legacy stipend terminology to `Monthly Cafeteria Credit`, `Company Subsidy`, and `Employee-Funded` amounts.
- Missing funding no longer looks like a legitimate zero balance; the UI explicitly explains the missing input.
- Manual reference refresh now refreshes employees, holidays, and company subsidy policy together rather than employees only.
- Reference-data dialog wording was updated to reflect the full refresh scope.

## Legacy cleanup

- Removed unused `CreditRateScheduleRow` from the active EF model.
- Removed the unused central `SaveCreditRateScheduleAsync` path and `CreditRateSchedules` DbSet/mapping.
- Existing old database tables/columns can remain on disk; the runtime no longer maps or writes the obsolete monthly rate schedule table.
- Historical `Stipend` transaction/domain concepts and compatibility allocation columns were retained where they still represent real persisted behavior; they were not deleted simply for naming cleanup.
- FFP durable ledger outbox remains because it is an active reliability mechanism, not legacy dead code.

## Tests

- Customer account query tests now verify authoritative funding status and the cached employee credit/company subsidy values.
- Added coverage proving that an employee credit snapshot with a missing `FFP00` policy returns an explicit unavailable-funding status without crashing or fabricating spendable credit.
- Weekly Aegis messaging regression test verifies `EmployeeId`.

## Verification

- JSON and project XML parse checks passed.
- Static C# delimiter and stale-member scans passed.
- No active `ICreditRateProvider`, `CreditRateQuery`, `CreditRateScheduleRow`, or old missing stipend/subsidy exception remains.
- `dotnet build` / `dotnet test` could not be run in the generation environment because the .NET SDK is not installed.
