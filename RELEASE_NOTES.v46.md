# Aegis.Cafeteria v46

## Test and contract corrections

- Updated `CustomerIdentificationTests.StubAccountQuery` to implement `ICustomerAccountQuery.GetTransactionsAsync`.
- Corrected stale prior-period settlement tests to match the current greenfield debt model:
  - prior-period settlements do not receive company subsidy;
  - they remain employee liability and therefore contribute to payroll deduction until paid;
  - prior-period debt may exceed the current month's base credit, in which case current purchase availability is reduced to zero rather than throwing.
- No production database schema change; local schema remains version 42.
