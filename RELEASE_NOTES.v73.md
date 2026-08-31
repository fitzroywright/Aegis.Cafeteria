# Aegis.Cafeteria v73

## Aegis.Cafeteria.Services

- Added `Aegis.Cafeteria.Services` ASP.NET Core/worker host.
- Added durable service event store with monotonic sequence and idempotent `OperationId`.
- Added Management APIs for Additional Credit and Stipend issuance.
- Added POS management-event pull/inbox; Additional Credit and Stipends become normal local employee transaction-history events.
- Added FFP projections for synchronized Additional Credit and Stipend Card Reduction with original management username preserved.
- Added centralized FFP proxy endpoints. When Services is enabled, POS reference refresh and local FFP outbox publication route through Services instead of direct FFP SQL.
- Added scheduled month-opening allocation worker using shared domain calculation, FFP Holidays, FFP00.Price (Credit Limit) and FFP00.Cost (Subsidy).
- Added optional daily management summary email and weekly employee FFP-ledger transaction emails; dispatch state is durable.
- Added optional API-key protection for all Services `/api/*` endpoints.
- Removed the weekly messaging hosted worker from POS startup; scheduled cross-system messaging belongs in Services.

## POS persistence

- Added durable `ManagementEventInbox` with sequence/operation ID tracking.
- Local SQLite schema is now 73. Because the current development deployment model is greenfield, delete/archive the previous local development DB before first v73 run.

## Rollout safety

- `CafeteriaServices:Enabled` defaults to false.
- `UseServiceMonthlyAllocations` defaults to false so the already-proven v72 POS can be validated against Services before month-opening authority is switched.
- POS sales remain local-first and do not require Services/FFP availability.
