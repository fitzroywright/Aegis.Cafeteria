# Aegis.Cafeteria v64

## Funding correction

- Removed `DailyCreditRate` from the domain/application/UI contracts.
- Removed the highest-saleable-LunchItem-price funding rule.
- `FFP00.Price` is now the sole cafeteria funding input.
- Monthly total credit is calculated once as `EligibleWorkingDays × FFP00.Price`.
- Ordinary LunchItem prices affect sales only.
- Existing SQLite schema 48 is retained: legacy physical column names are mapped for compatibility and do not represent separate funding policies.
- Existing allocations written by the removed two-rate model are recognized by their two stored values and corrected once to the original FFP00.Price without deleting transaction history.
- Funding UI no longer displays a daily-credit rate.
