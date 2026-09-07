# Architecture

## Runtime boundary

The cafeteria platform uses a local-first point-of-sale model with a central service layer. Cashier terminals run the POS client and retain local operational data. The service layer owns management operations, central FFP database access, publication workers, audit trail and health diagnostics.

## Components

1. POS clients
   - Windows and Linux clients provide the cashier workflow.
   - Terminals support NFC, code, TRN and name customer lookup.
   - Sales, refunds, voids, split tender and customer payments are completed at the counter.

2. Service layer
   - Exposes management and integration endpoints.
   - Stores service state, audit records, policy data and publication events.
   - Publishes completed financial activity to FFP ledger and balance projection data.

3. Management web area
   - Separates supervisory activity from cashier activity.
   - Supports funding policy, additional credit, stipend or card reduction, month opening, backup, restore and audit review.

4. FFP data sources
   - Employee eligibility, holiday calendar, ledger and balance projection data remain in the FFP environment.
   - The service layer is the only application component that connects directly to the central FFP database.

## Data ownership

- POS SQLite owns cashier operating state, product catalog, local transaction snapshots and terminal behavior.
- Service SQLite owns management state, policy records, event publication status and audit trail.
- FFP SQL remains the source for employee eligibility, holidays and published ledger or balance projection records.

## Financial rule ownership

The service and shared business logic own cafeteria financial rules. The current policy calculates monthly credit from eligible working days and configured daily rates. Allocation records snapshot the rates used so historical reports do not change when rates change later.

## Reliability design

The POS remains useful during temporary network disruption because counter work is local-first. The service layer and background workers handle central publication and operational integration. Health and diagnostic endpoints support support-team verification.
