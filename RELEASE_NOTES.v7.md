# Aegis.Cafeteria v7

## Customer account view

v7 adds a dedicated customer-identification read model for the future terminal UI.

`ICustomerAccountQuery.GetAsync(customerId, year, month)` returns `CustomerAccountView` with:

- customer identity and photo;
- account-period identity and closed status;
- current-month purchase/refund/account-event history;
- comprehensive calculated financial status;
- synchronization/online status.

### Financial display values

The read model exposes:

- Credit Limit
- Subsidy Limit
- Subsidy Used
- Subsidy Amount Remaining
- Non-Subsidy Amount Remaining
- Total Available to Spend
- Total Order Sales
- Cafeteria Account Spending
- Cash Spending
- Additional Credit Approved / Used / Remaining
- Stipend Cash Paid / Card Reduction
- Prior Period Settlements
- Prior Period Refund Credits
- Customer Payments
- Customer Responsibility
- Payroll Deduction Required
- Total Refunds

No displayed balance is persisted as mutable account state. Values are recalculated from customer configuration, the account period, orders, refunds and account events.

### Provider-neutral monthly query optimization

Completed orders now persist `AccountYear` and `AccountMonth`, with a composite index on customer/period. This avoids database-provider-specific date-part extraction and keeps the same query path usable with SQLite, SQL Server and PostgreSQL.

### Tests

Added unit tests for financial display partitioning and persistence tests for the full customer-account query shape. The test runner scripts remain `scripts/test-all.ps1` and `scripts/test-all.sh`.

### Existing v6 correction

Corrected `CustomerAccountStatementService` to use `PriorPeriodOutstandingSettlement.ApprovedAt` rather than a nonexistent `RecordedAt` property.
