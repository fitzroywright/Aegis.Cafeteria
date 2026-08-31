# Aegis.Cafeteria v3

## Development Reset

v3 adds a protected Development-only reset operation:

`POST /api/development/reset`

The reset is intended for the JMDB01/FFP integration-test environment. It requires the existing Windows/AD Cafeteria management authorization and is not mapped in Production.

A reset:

- removes Aegis.Cafeteria ledger rows from `dbo.EmployeeIDScans` for Tickets Assigned, allocation recalculation, purchases, refunds, customer payments, Additional Credit and stipend reductions;
- resets `Employees.CreditLimit` and `Employees.AvailableBalance` to zero for eligible cafeteria employees;
- removes previous Services financial/management events while preserving a monotonic reset event for POS synchronization;
- clears Services Additional Credit policies, scheduled-dispatch history and funding-policy history;
- preserves the current authoritative funding policy and its FFP00 compatibility mirror;
- instructs connected/offline POS terminals to clear local employees, identifiers, holidays, allocations, orders, refunds, payments, account events, account periods and FFP outbox state;
- preserves the POS-local product catalog;
- rejects stale pre-reset POS ledger publications so an old outbox cannot recreate data after the reset.

After reset, allow the POS reference refresh to repopulate eligible employees, verify the funding policy, and run Month Opening again.
