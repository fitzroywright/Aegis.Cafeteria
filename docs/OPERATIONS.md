# Operations — v88

## Daily POS operation

1. Operator signs in using AD/Common.Security.
2. POS refreshes employee/product/holiday reference data through Services.
3. Cashier identifies employee by NFC, Code, TRN or Name.
4. POS shows Available to Spend and Payroll Deduction to the employee/customer display.
5. Cashier adds products and completes tender.
6. Cash sales and Cafeteria Account sales are persisted locally first.
7. FFP projection is published from the durable outbox; temporary network loss does not invalidate a completed local sale.

## Void

`Void Current Sale` is available only for an active, uncompleted sale with items. It requires manager authorization and a reason. ESC is not a void; ESC abandons in-memory state and creates no order/audit ledger entry.

## Refund

Refunds apply only to completed orders. Windows and Linux support full or partial item/quantity refunds with tender-safe allocation. Manager authorization and reason are required.

## Customer Payment

`Customer Payment...` records money received from an employee against current Payroll Deduction. It captures method/reference/operator/time and reduces Payroll Deduction. The employee financial summary shows Customer Responsibility (before payments), Customer Payments and Payroll Deduction separately.

## Additional Credit

Additional Credit is a management-side operation. In production it should be initiated from FFP Manager through Cafeteria Services, not from a POS terminal. Approval is capped by the employee-specific Additional Credit Limit and does not increase company subsidy.

## Month opening

Services generates immutable monthly allocations from FFP employee eligibility, Holidays and LunchItems policy values. Automatic execution is controlled by `MonthOpening`. Manual authorized execution uses `/api/month-opening/run`.

## Monitoring

Check:

- Services `/health`;
- POS heartbeat JSON;
- Graylog if enabled;
- POS FFP outbox failures/poison attempts;
- Services FFP publication failure state;
- stale employee/product reference cache warnings.

## Backup

Back up the POS SQLite database and the Services SQLite database. A POS database contains financial/audit history and is not merely a cache.

## Disaster recovery principle

A local POS can continue sales during temporary Services/FFP failure using its cache. Management operations that require Services should be deferred until Services is available; do not bypass the service by editing FFP balances manually.


## v91 split tender and refund workflow

- During split tender, entering cash automatically recalculates the Cafeteria Account amount as the remaining sale value, capped by the employee's available cafeteria-account capacity.
- If the employee does not have enough cafeteria-account capacity to cover the non-cash balance, the Cafeteria Account amount remains at its maximum and Remaining continues to show the cash still required.
- Cash above the remaining sale value is treated as cash received and produces Change Due; it does not create a negative Cafeteria Account amount.
- Refund authorization opens with a factual generated reason based on the selected item quantities and original sale time. The manager may edit or replace that reason before confirming the refund.
