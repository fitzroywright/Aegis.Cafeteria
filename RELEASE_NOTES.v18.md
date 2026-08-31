# Aegis.Cafeteria v18

- Fix SQLite synchronization failure in `CleanupOutboxAsync`: `DateTimeOffset` cutoff comparison now occurs in memory after SQL filters succeeded rows by state.
- Fix `CarriedDebtPaymentAvailabilityTests` to use a fixed September 2026 timestamp instead of `DateTimeOffset.UtcNow`.
- Includes all fixes from v17 and earlier.
- Static checks only in generation environment; run `dotnet build` and tests on the Windows development machine.
