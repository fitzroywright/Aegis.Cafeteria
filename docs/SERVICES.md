# Services

## Responsibilities

The service layer is the production integration boundary. It owns:

- central FFP database access;
- management API;
- funding-policy storage;
- additional credit, stipend or card reduction and month-opening operations;
- durable publication events;
- audit trail;
- health and diagnostic endpoints;
- backup and restore support.

## Endpoint areas

- Management operations: additional credit, stipend or card reduction, month opening, policy maintenance and audit query.
- Development operations: coordinated development reset when running in development.
- Health operations: startup and service-health verification.
- Integration operations: publication of cafeteria financial activity to FFP ledger and balance projection records.

## Swagger behavior

Swagger and OpenAPI are available only in development. The audit endpoint mapper now guards duplicate registration so the OpenAPI document is generated cleanly when development Swagger is enabled.

## Background work

Background workers handle service heartbeat, publication and related support operations. Worker failures should be investigated from service logs without assuming the cashier lane has failed.
