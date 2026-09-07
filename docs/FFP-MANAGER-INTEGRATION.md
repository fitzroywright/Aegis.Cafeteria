# FFP Manager Integration for Cafeteria Management

FFP Manager calls the cafeteria service layer for cafeteria management actions. It should not directly modify POS SQLite, employee credit balances or cafeteria financial state.

## Service boundary

The service layer validates authorization, applies cafeteria business rules, records durable events, updates audit records and publishes compatibility projections to FFP data where required.

## Management actions

- Funding-policy maintenance.
- Additional credit approval.
- Stipend or card reduction.
- Manual month opening.
- Audit review.

## Reason for the boundary

A single service boundary prevents duplicate financial logic, preserves auditability, protects closed periods and keeps cashier activity separate from management authority.

## Operational flow

1. FFP Manager user performs the management action.
2. FFP Manager calls the cafeteria service endpoint.
3. The service validates identity, authorization and business rules.
4. The service records the event and audit entry.
5. The service publishes the resulting FFP projection when applicable.
