# Aegis.Cafeteria v2

- Adds a Windows-authenticated management endpoint for safely recalculating an already-opened month: `POST /api/month-opening/recalculate?year=YYYY&month=M`.
- Recalculation uses the current Services funding policy, current FFP holidays and the current eligible-employee set.
- Recalculation is blocked if the month contains Cafeteria purchases, refunds, prior-period refunds, customer payments, Additional Credit, or stipend card reductions.
- Recalculation is also blocked unless each employee still has the untouched FFP Month Opening `CreditLimit` and `AvailableBalance`, preventing accidental overwrite of live financial activity.
- Recalculation is blocked if the eligible employee set changed after Month Opening; employee entitlement changes must be handled explicitly instead of silently during a rate correction.
- The original Month Opening event remains immutable for audit history. A new `MonthlyAllocationRecalculation` service event becomes the authoritative replacement allocation.
- FFP receives an auditable `Monthly Allocation Recalculation` ledger entry containing the credit-limit delta and the new post-recalculation CreditLimit/AvailableBalance.
- POS terminals consume the durable replacement event and update their local monthly allocation snapshot; polling/SignalR reliability behavior is unchanged.
- Payroll liability reports resolve the latest allocation/recalculation event for the requested month.
- Repeating recalculation with the same working days/rates is idempotent and creates no additional replacement event.
