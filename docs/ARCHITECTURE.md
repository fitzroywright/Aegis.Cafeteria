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

A Cafeteria Account tender can consume company subsidy and then create employee-funded responsibility. Current-period responsibility is expected to be settled by customer payments and/or payroll deduction. Only unresolved prior-period amounts become carried-forward debt.

The UI therefore distinguishes:

- Customer Responsibility (before payments)
- Customer Payments
- Payroll Deduction
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

Windows and Linux share domain/application/infrastructure/reporting code and are expected to expose the same business capabilities: identification, sales, tendering, void, partial refunds, Customer Payment, payroll-deduction visibility, second display, dashboard, reports and export.
