# Design Document

## Goals

Aegis.Cafeteria provides a resilient cafeteria POS and management platform with Windows and Linux POS clients, centralized Cafeteria Services, controlled FFP integration, auditability, reporting, scheduled email, diagnostics, and customer-facing display support.

## Major components

### Domain
Contains cafeteria business concepts and rules. Domain code should remain independent of UI and transport concerns.

### Application
Contains use cases, account/customer abstractions, orchestration interfaces, and application-level services.

### Infrastructure
Contains persistence, FFP SQL integration, Active Directory/Common.Security support, logging, messaging, health, configuration, and external adapters.

### Cafeteria Services
Owns authoritative integrations and management mutations. It exposes APIs for POS and Management clients, runs month-opening and scheduled-email workers, persists audit records, sends GELF logs, and performs trusted FFP SQL access.

### Windows POS
Primary WinForms POS. It communicates with Cafeteria Services and must not contain a legacy direct-SQL fallback.

### Linux POS
Avalonia-based POS intended to preserve the same business behavior as Windows while using Linux-native hosting/startup conventions.

### Management Console
Browser-based management UI. It authenticates through Common.Security via Cafeteria Services and exposes controlled management actions rather than direct database access.

### Reporting
Provides report models/exporters used by management and scheduled reporting.

## Security boundaries

The POS and Management clients are not trusted with FFP SQL credentials. Cafeteria Services is the trusted integration boundary. SQL authentication is Windows Integrated Security rather than an application-stored password.

Management authorization is separate from simple authentication. Management operations require the configured authorized groups/session token path.

## Financial behavior

Monthly credit is based on eligible working days and configured funding rates. Company subsidy cannot exceed the monthly credit limit. Debt carries forward; unused positive credit does not automatically carry forward. Additional credit, stipends, customer payments, refunds, payroll deductions, and prior-period adjustments are represented as explicit business/account events.

Outstanding Balance on the main/customer views excludes payroll deduction when calculating what remains payable outside payroll. Customer cash payments reduce outstanding balance and are displayed as a payment row with pre-payment balance, cash received, and post-payment balance.

## Audit model

Audit is dual-destination:

1. Durable AuditTrail rows in the Services database.
2. Structured GELF events to Graylog.

HTTP mutation auditing records actor, action, entity, customer/operation identifiers when available, terminal/correlation data, status, and success/failure. Important business operations may additionally record before/after snapshots.

## Logging

All application logging uses GELF UDP for centralized Graylog ingestion. The configured endpoint is 10.10.0.3:1699. Syslog UDP is not part of this release baseline.

## Resilience principles

- A Graylog outage must not fail a sale or management operation.
- Audit/log failures must not replace the business response.
- POS startup can communicate with Services while the splash screen remains visible.
- Customer photos remain cacheable for resilience/performance.
- FFP access belongs in Services so transient FFP problems do not recreate direct SQL coupling in the POS.
