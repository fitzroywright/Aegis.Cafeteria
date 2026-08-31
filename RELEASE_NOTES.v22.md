# Aegis.Cafeteria v22

## Reporting module

- Added new `Aegis.Cafeteria.Reporting` project with no DevExpress dependency.
- Added report catalog and reusable `ReportDocument`, metric, table and chart models.
- Added Executive Summary, Daily Sales, Sales Trend, Tender Mix, Product Sales, Employee Statement, Monthly Allocations, Subsidy Utilization, Customer Responsibility, Refund/Void Audit, Additional Credit Audit, Outstanding Debt and FFP Reconciliation reports.
- Added line, bar and pie chart datasets.
- Added dependency-free printable HTML exporter with inline vector SVG charts.
- Added CSV exporter.
- Added reporting test project covering HTML, CSV and all supported chart renderers.
- Registered reporting services in the Windows POS host for the forthcoming polished Reports UI.

## Regression protections retained

All v20/v21 fixes remain present, including SQLite-safe `DateTimeOffset` handling, `OperationId` outbox ordering, Development secrets without Bitwarden, startup path creation/diagnostics, Graylog logging, FFP durable/idempotent publication and Zabbix heartbeat support.

## Build verification

Project/solution XML and JSON configuration parsing passed. Existing static regression audit passed. The generation environment does not provide a .NET SDK, so `dotnet build` and `dotnet test` could not be executed here and must be run on a .NET 10 development machine.
