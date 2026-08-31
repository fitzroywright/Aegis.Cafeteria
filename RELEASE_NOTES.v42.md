# Aegis.Cafeteria v42

## Architecture reset

- Removed the Aegis central SQL/PostgreSQL database and central EF context.
- Removed central Inbox/ChangeJournal/outbox synchronization, SignalR change distribution, TestHost server and central administration persistence.
- Local SQLite is the authoritative detailed POS database for the single-terminal deployment.
- FFP remains the corporate employee/reference system and receives employee financial projections through the durable FFP outbox.

## Legacy ticket isolation

- Removed `TicketsAssigned` and `TicketsRemaining` from every Aegis query, snapshot, cache and model.
- Removed FFP `CreditLimit` and `AvailableBalance` from the employee reference snapshot and monthly funding calculation.
- `EligibleForLunchTicket` is referenced only by `FfpSqlApplicationGateway`, where it becomes Aegis eligibility.
- Removed obsolete customer daily-rate/company-limit compatibility properties and the obsolete calculator overload.

## Aegis-owned monthly credit

- Added required `CafeteriaPolicy:EmployeeDailyCredit`. The greenfield configuration uses `0.00` as a fail-safe placeholder; an approved positive value must be set for FFP-enabled operation.
- Cached cafeteria policy now consists of Aegis employee daily credit plus FFP00 company subsidy daily rate.
- Monthly limit = eligible working days × Aegis employee daily credit.
- Allocation remains immutable after creation and is projected to FFP.

## Clean v42 SQLite

- Schema version is 42.
- Removed `CustomerFundingProfiles`, terminal central `Outbox`, `SyncState`, central Inbox and ChangeJournal structures.
- Retained FFP publication outbox, complete order/audit history, policy cache, monthly allocations and weekly dispatch state.
- Existing non-v42 databases fail closed and are never automatically modified/deleted.

## UI/operations

- POS status now reports FFP publication pending/poisoned operations rather than central synchronization.
- Second-screen customer display behavior from v38/v40 is retained.
- Common.Security, cashier/terminal audit, refunds/voids and Cash Customer behavior are retained.

## Tests/documentation

- Reworked allocation tests around Aegis-owned daily credit.
- Added guards proving no ticket/FFP-balance properties enter the workforce snapshot or Customer domain.
- Updated fresh-schema tests to assert removed central/funding-profile tables do not exist.
- Updated reference refresh, account-query, configuration, persistence, FFP integration and customer tests.
- Rewrote all current docs for the v42 architecture.
