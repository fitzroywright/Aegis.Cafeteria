# Architecture

## Runtime boundary

The Cafeteria platform uses a local-first point-of-sale model with a central service layer. Cashier terminals retain local operational state and durable outbound transaction queues. Cafeteria Services owns central Cafeteria state, audit history, financial event publication and diagnostics.

Organization-specific systems are reached only through a dynamically loaded Cafeteria integration plugin.

## Components

1. POS clients
   - Windows and Linux clients provide the cashier workflow.
   - Terminals support NFC, code, TRN and name customer lookup.
   - Sales, refunds, voids, split tender and customer payments are completed locally.
   - POS does not call organization-specific systems directly.

2. Cafeteria Services
   - Exposes management, POS synchronization and generic integration endpoints.
   - Stores Cafeteria-owned service state, audit records, financial events and publication state.
   - Loads the configured integration plugin at runtime from `IntegrationPlugin:AssemblyPath`.

3. Integration plugin
   - Implements the shared Cafeteria integration contracts.
   - Supplies policy facts and employee facts.
   - Publishes canonical Cafeteria transactions outward.
   - Contains no Cafeteria business-rule ownership.

4. Integration Plane and final adapter
   - Translate generic Cafeteria contracts to the organization-specific systems.
   - Organization-specific SQL, APIs and schema names belong only at this final boundary.

5. Management web area
   - Separates supervisory activity from cashier activity.
   - Supports additional credit, stipend/card reduction, month opening, reporting, audit review and other management operations.

## Data ownership

- POS local storage owns terminal operating state and durable local transaction queues.
- Cafeteria Services owns Cafeteria financial events, allocations, balances, management state, publication state and audit trail.
- Employee and policy facts are imported through the configured integration plugin.
- External systems are projection/integration targets, not Cafeteria's financial source of truth.

## Policy ownership

Cafeteria owns policy interpretation and allocation calculations.

The integration boundary supplies:

- daily credit-limit rate;
- daily company subsidy rate;
- authoritative working-day calendar;
- optional subsidy consumption mode;
- policy version and effective date.

The default subsidy consumption mode is `SubsidyConsumptionMode.Standard` when an adapter omits the field.

## Reliability design

The POS remains operational during temporary service or integration outages because counter work is local-first.

Cafeteria Services uses durable events and retryable publication. External publication failure does not redefine Cafeteria balances or reporting state.

## Plugin loading

Cafeteria Services does not reference a concrete integration plugin at compile time.

At startup it loads the assembly configured by:

```text
IntegrationPlugin:AssemblyPath
```

The plugin must provide exactly one implementation of the required Cafeteria integration contracts. Missing or ambiguous implementations cause startup failure rather than silent fallback.
