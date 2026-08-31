# Operations — v88

## Daily POS operation

1. Operator signs in using AD/Common.Security.
2. POS refreshes employee/product/holiday reference data through Services.
3. Cashier identifies employee by NFC, Code, TRN or Name.
4. POS shows Available to Spend and Employee Amount Due to the employee/customer display.
5. Cashier adds products and completes tender.
6. Cash sales and Cafeteria Account sales are persisted locally first.
7. FFP projection is published from the durable outbox; temporary network loss does not invalidate a completed local sale.

## Void

`Void Current Sale` is available only for an active, uncompleted sale with items. It requires manager authorization and a reason. ESC is not a void; ESC abandons in-memory state and creates no order/audit ledger entry.

## Refund

Refunds apply only to completed orders. Windows and Linux support full or partial item/quantity refunds with tender-safe allocation. Manager authorization and reason are required.

## Customer Payment

`Customer Payment...` records money received from an employee against current Employee Amount Due. It captures method/reference/operator/time and reduces Employee Amount Due. The employee financial summary shows Customer Responsibility (before payments), Customer Payments and Employee Amount Due separately.

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

## v92 management-event delivery

The production POS keeps an outbound SignalR connection to Cafeteria Services over the same HTTPS service endpoint. No inbound firewall rule from Services to the POS is required. SignalR is only a low-latency wake-up channel; the durable event endpoint and local management-event inbox remain the source of reliable delivery and offline recovery.

## Employee Amount Due

The POS displays **Employee Amount Due**. This is a cafeteria liability figure only. It is not a payroll deduction record and does not mean payroll has processed or collected the amount.

Customer Payment reduces Employee Amount Due. Payroll processing, deduction tracking, collection reconciliation and any resulting prior-period balance are managed outside the POS.

## Payroll Employee Amount Due notification

Cafeteria does not track Payroll deductions or collections. Aegis.Cafeteria.Services calculates the prior month's Cafeteria **Employee Amount Due** and emails Payroll a report package.

Configure `ScheduledEmail:PayrollRecipients`, `ScheduledEmail:PayrollNotificationEnabled`, and `ScheduledEmail:PayrollReportHourLocal`. After the configured hour, Services sends the previous month once and records `payroll:YYYY-MM` in `ScheduledDispatches` so restarts do not duplicate normal delivery.

The email includes:
- `Cafeteria-Payroll-Detail-YYYY-MM.csv` — one row per employee with a positive amount due and the calculation components;
- `Cafeteria-Payroll-Summary-YYYY-MM.csv` — department totals and an overall total.

Administrative preview: `GET /api/management/payroll-report?year=YYYY&month=MM`.
Manual send/re-send: `POST /api/management/payroll-report/send?year=YYYY&month=MM&force=true`.

The reports are instructions/information to Payroll only. Cafeteria does not record whether Payroll deducted or collected the amounts.


## v98 product and workforce boundary

Only employees with `FFP.Employees.EligibleForLunchTicket = 1` are active Cafeteria employees. The POS does not load ineligible employees for browse, name search or identifier matching. Historical customer rows may remain locally only to support old transaction history.

Saleable cafeteria products are owned by the POS SQLite database. `FFP.dbo.LunchItems` and Cafeteria Services are not product-catalog sources. Product administration changes the current POS catalog only; completed order lines preserve ProductName, UnitPrice and Quantity as immutable transaction snapshots. Reports use those transaction/ledger values rather than looking up today's product price.

`FFP00` is retained solely as a compatibility mirror for the Services-owned funding policy and must not be treated as a saleable product or catalog source.
