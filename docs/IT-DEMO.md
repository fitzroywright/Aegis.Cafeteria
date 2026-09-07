# IT Demo Narrative

## Audience

FFP IT.

## Story

The demo explains the cashier workflow and the technical architecture that supports it: local-first terminals, service-owned integration, data ownership, management authorization, audit trail, reporting, deployment and support points.

## Technical coverage

- POS terminal architecture and local SQLite operation.
- Service API, service store, management endpoints and health endpoint.
- FFP employee, holiday, ledger and balance projection integration.
- Financial rule engine for monthly allocation, subsidy cap, debt carry-forward and refund behavior.
- Development Swagger and duplicate audit-route registration guard.
- Audit middleware for mutating operations.
- Secret reference model for production configuration.
- Build gate, static audit and acceptance-test coverage.

## Outstanding technical follow-up

- Capture full stack trace for the repeated worker I/O exception seen in service logs.
- Finalize production secret permissions and rotation process.
- Complete production-like acceptance testing across cashier, management, reporting and FFP publication workflows.
- Review least-privilege role membership for management endpoints.
- Finalize monitoring and alerting for service health, worker failures and publication backlog.
