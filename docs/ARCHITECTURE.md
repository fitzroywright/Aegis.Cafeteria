# Architecture — v88

## Production deployment boundary

Aegis.Cafeteria is local-first at the POS, with a central integration/background service.

```text
Windows POS / Linux POS
  local SQLite: orders, refunds, voids, payments, local audit, outbox, cache
        |
        | HTTPS + service API key
        v
Aegis.Cafeteria.Services
  service SQLite: management events, employee credit policy, publication state
        |
        +----> FFP SQL Server
        |
        +<---- FFP Manager via Windows Integrated Authentication
```

## System ownership

### FFP owns

- employee identity and eligibility (`Employees`, `Departments`);
- active cafeteria products and funding policy (`LunchItems`);
- holidays (`Holidays`);
- compatibility employee cafeteria ledger (`EmployeeIDScans`);
- compatibility summary fields (`Employees.CreditLimit`, `Employees.AvailableBalance`).

### POS owns

- completed sale detail;
- tender split;
- cash-customer transactions;
- void/refund/customer-payment audit;
- local reports;
- offline operation and durable POS outbox.

### Cafeteria Services owns

- management-originated Additional Credit events;
- per-employee Additional Credit Limit policy;
- stipend events;
- month-opening allocation generation;
- central FFP SQL access in the recommended production topology;
- durable publication of management/month-opening events;
- scheduled management/employee email.

## Funding model

Default policy code is `FFP00` for both dimensions, configurable independently.

- `MonthlyCreditLimit = EligibleWorkingDays × CreditLimitPolicy.Price`
- `MonthlySubsidy = min(MonthlyCreditLimit, EligibleWorkingDays × SubsidyPolicy.Cost)`
- employee-funded portion = Credit Limit − Subsidy

Additional Credit increases spending capacity only. It does not increase subsidy.

## Customer responsibility and debt

A Cafeteria Account tender can consume company subsidy and then create employee-funded responsibility. Current-period responsibility is expected to be settled by customer payments and/or employee amount due. Only unresolved prior-period amounts become carried-forward debt.

The UI therefore distinguishes:

- Customer Responsibility (before payments)
- Customer Payments
- Employee Amount Due
- Carried Debt

## Idempotency

Every external financial event uses a GUID operation ID. FFP publication writes that value to `EmployeeIDScans.TransmissionID`. Service-originated events also retain the operation ID in Services SQLite. Retries must reuse the same operation ID.

## Security

- POS operator/manager authorization: Common.Security over LDAPS/AD.
- FFP Manager management API: Windows Integrated Authentication/Negotiate.
- POS-to-Services endpoints: `X-Cafeteria-Api-Key` resolved from Bitwarden Secrets Manager in production.
- FFP DB connection string: Bitwarden Secrets Manager in production.
- Production fails closed if Services mode is enabled but the service API key cannot be resolved.

## UI parity

Windows and Linux share domain/application/infrastructure/reporting code and are expected to expose the same business capabilities: identification, sales, tendering, void, partial refunds, Customer Payment, employee-amount-due visibility, second display, dashboard, reports and export.

## v92 real-time synchronization architecture

Management and month-opening changes are still represented by immutable durable service events. SignalR is layered on top only as a low-latency notification channel. The sequence is: commit service event, broadcast `EventsAvailable`, POS performs the normal sequence-based event pull, POS applies the event idempotently, POS records the inbox checkpoint, then POS notifies the UI that the affected account changed.

Because polling remains active, SignalR disconnection does not change correctness or offline behavior. Reconnection also wakes the durable pull immediately.

## Employee liability boundary (v95)

Aegis.Cafeteria calculates **Employee Amount Due** as the employee's cafeteria liability after voluntary customer payments. The POS does not create payroll deduction records, track payroll collections, reconcile payroll results, or derive carry-forward debt from payroll results. Those responsibilities belong to FFP Manager and/or the payroll system.

A closed cafeteria period may snapshot Employee Amount Due so an external management/payroll process can consume a stable figure. Any prior-period balance later supplied to Cafeteria is treated as an external prior-period obligation, not as a payroll reconciliation performed by the POS.


## v98 product and workforce boundary

Only employees with `FFP.Employees.EligibleForLunchTicket = 1` are active Cafeteria employees. The POS does not load ineligible employees for browse, name search or identifier matching. Historical customer rows may remain locally only to support old transaction history.

Saleable cafeteria products are owned by the POS SQLite database. `FFP.dbo.LunchItems` and Cafeteria Services are not product-catalog sources. Product administration changes the current POS catalog only; completed order lines preserve ProductName, UnitPrice and Quantity as immutable transaction snapshots. Reports use those transaction/ledger values rather than looking up today's product price.

`FFP00` is retained solely as a compatibility mirror for the Services-owned funding policy and must not be treated as a saleable product or catalog source.
