# Aegis.Cafeteria v47

## FFP LunchItems rate authority restored

v47 corrects an over-aggressive greenfield cleanup that had moved the employee daily cafeteria credit amount into local configuration.

- Monthly credit is again derived from FFP `LunchItems.Price`.
- `FfpIntegration:DailyCreditLunchItemCode` identifies the rate row; default code is `FFP01` and can be overridden without duplicating the amount locally.
- `FFP00.Price` remains the company subsidy calculation input and is never a sale item.
- `CafeteriaPolicy:EmployeeDailyCredit` / `DailyCreditRate` local amount configuration is removed.
- `Employees.TicketsAssigned`, `TicketsRemaining`, existing `CreditLimit`, and existing `AvailableBalance` remain excluded from entitlement calculations.
- FFP `Employees.CreditLimit` and `AvailableBalance` remain write-side compatibility projections.

## UI privacy/policy change

The company subsidy **per-day rate is no longer exposed through UI account/funding view models** and is removed from Windows/Linux funding status text. Subsidy allocation/used/remaining amounts remain available because they are financial account results rather than the policy rate itself.

## Clean rate-policy model and schema

- `CafeteriaFundingPolicy` was replaced by `CafeteriaRatePolicy`.
- The local `CafeteriaRatePolicy` table stores the FFP daily-credit Lunch Item code/rate, the internal FFP00 subsidy rate, and refresh timestamp.
- Local SQLite schema version is now **47**. Older local files are refused; the application contains no automatic delete/reset path.
- Historical monthly allocations snapshot the rates used at calculation time and remain immutable.

## Additional cleanup

- Fixed the Monthly Allocations report's stale `Stipend` column label to `Employee-funded`.
- Renamed internal reconciliation keys from `aegisLimit/aegisBalance` to neutral `cafeteriaLimit/cafeteriaBalance`.
- Renamed internal messaging contracts from `IAegisMessagingGateway` / `AegisEmployeeMessage` to neutral Cafeteria names.
- Updated current README, architecture, FFP integration, operations, deployment, reporting, weekly-messaging, UI, and testing documentation.
- Added regression coverage that UI-facing funding contracts cannot expose `CompanySubsidyDailyRate`.
