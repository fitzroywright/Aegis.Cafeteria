# Aegis.Cafeteria v29

## Customer funding fix

- Removed the legacy month-based `ICreditRateProvider` path from `MonthlyCreditAllocationService`.
- Customer identification, account browsing, administration and monthly allocation now have one funding path only: `ICustomerFundingProfileProvider`.
- Removed the obsolete local/central credit-rate provider classes and the old `CreditRateSchedule` application/domain contract from active runtime code.
- The previous exception `No stipend/subsidy credit-rate schedule exists for yyyy-MM` can no longer originate from customer identification.
- Missing external funding data returns a temporary zero-credit view without persisting a zero monthly allocation; a later successful refresh can create the real allocation.

## FFP adapter fixes

- Fixed FFP employee snapshot mapping to use `employeeId` as the source-neutral customer id.
- Fixed deterministic cached identifier IDs to hash the supplied `customerId`.
- Canonical FFP connection remains `Data Source=JMDB01;Initial Catalog=FFP;Integrated Security=true;Pooling=false;Encrypt=false`.
- FFP employee `CreditLimit` populates the customer funding profile.
- Active `LunchItems.Code = 'FFP00'` `Price` populates the company subsidy daily rate.

## Tests

- Converted allocation/customer account tests away from legacy rate schedules to source-neutral customer funding profiles.
- Existing legacy `CreditRateScheduleRow` persistence/sync structures remain only for backward database compatibility and are not an active allocation dependency.

## Verification

- Static source scan confirms no active `ICreditRateProvider`, `EfCreditRateProvider`, `EfCentralCreditRateProvider`, or legacy missing-rate exception remains.
- JSON configuration and ZIP integrity verified.
- `dotnet build` / `dotnet test` were not run in the generation environment because the .NET SDK is unavailable there.
