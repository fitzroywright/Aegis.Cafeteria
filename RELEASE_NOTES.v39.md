# Aegis.Cafeteria v39

## Fixed
- Added an idempotent local SQLite schema upgrade for `Orders.CashierUserId` and `Orders.TerminalId`.
- Existing POS databases created before v36 are upgraded in place at startup; transaction history and pending outbox data do not need to be deleted.
- Fixes Cash Customer and employee checkout failures such as `SQLite Error 1: no such column: CashierUserId` after upgrading an existing installation to v36-v38.

## Why this was required
`Database.EnsureCreated()` creates a schema only when the database is new. It does not add newly introduced columns to an existing SQLite table. v36 added cashier/terminal audit fields but the local additive upgrade path was not updated.
