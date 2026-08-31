# v48 — FFP LunchItems single-source rate fix and UI safeguards

- Removed the invented `FFP01` daily-credit policy convention and the `DailyCreditLunchItemCode` configuration option.
- Confirmed `FFP00` is the only active special FFP0x code used by Cafeteria.
- FFP LunchItems are now loaded once per catalog refresh and that same snapshot supplies both saleable products and cafeteria rates.
- Saleable prices and the daily credit rate use **`LunchItems.Price` only**; `Cost` is not selected or used.
- Daily credit rate = highest positive active saleable `LunchItems.Price` (excluding `FFP00`).
- `FFP00.Price` remains the internal company-subsidy rate and is not shown in the UI.
- Added a Yes/No confirmation before user-initiated main POS shutdown.
- Restyled the Windows operator login to match the splash screen's navy/orange/white theme.
- Updated rate-policy persistence to remove the obsolete code field and bumped the greenfield local schema to 48.
- Added/updated tests for single-snapshot product/rate refresh, rate derivation, FFP00-only special-code policy, and configuration cleanup.
