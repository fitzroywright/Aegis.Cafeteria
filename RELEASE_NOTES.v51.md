# Aegis.Cafeteria v51

## Reports menu and report runner

- Added a Windows POS **Reports** menu.
- Added grouped report navigation: Overview, Sales, Employee & Credit, Audit & Reconciliation.
- Added a reusable Report Center / runner dialog with month-to-date defaults.
- Added employee filtering through the existing live employee search dialog.
- Added product filtering for Product Sales.
- Added optional voided-order inclusion where relevant.
- Added HTML preview through the default browser.
- Added Save HTML and Save CSV actions.
- Exposed all 13 supported reports through the menu.
- Centralized report catalog metadata in `CafeteriaReportCatalog`.
- Added regression tests ensuring every `CafeteriaReportKind` has exactly one catalog entry.

No database schema change. SQLite schema remains version 48.
