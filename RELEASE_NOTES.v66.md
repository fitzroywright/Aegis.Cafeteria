# Aegis.Cafeteria v66

## Greenfield funding-policy correction

v66 assumes the local SQLite database is deleted/archived before first run and creates schema version 66.

- Credit Limit policy is read from a configurable FFP LunchItems code, default `FFP02`.
- Subsidy policy is read from a configurable FFP LunchItems code, default `FFP00`.
- `Monthly Credit Limit = Eligible Working Days x CreditLimitLunchItem.Price`.
- `Monthly Subsidy = min(Monthly Credit Limit, Eligible Working Days x SubsidyLunchItem.Price)`.
- Both policy rows are excluded from the saleable product catalog.
- The configured codes and both prices are cached in SQLite for offline month-opening calculations.
- Restored the identifier normalization guard: if NFC/Code/TRN normalization is null/empty, exact identification returns null before querying SQLite. This prevents non-TRN name text from matching an empty TRN identifier row.
- Windows and Linux app settings expose `FfpIntegration:CreditLimitLunchItemCode` and `FfpIntegration:SubsidyLunchItemCode`.
- The Windows `Cafeteria Development` launch profile remains included and sets `DOTNET_ENVIRONMENT=Development`.

There is no automatic migration. Stop Cafeteria and delete/archive the old `cafeteria.local.db`, `-wal`, and `-shm` files before starting v66.
