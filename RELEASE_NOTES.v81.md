# Aegis.Cafeteria v81

## Reporting exports and Report Center polish

- Added dependency-free Excel `.xlsx` export for every report.
  - Summary sheet contains report metadata and KPI metrics.
  - Each report table is exported to its own worksheet.
  - Chart source data is exported to a Chart Data worksheet when present.
  - Numeric/currency cells are written as numeric Excel cells rather than display-only text.
- Added dependency-free PDF `.pdf` export for every report.
  - Produces printable multi-page report output without requiring Microsoft Office, browser automation, or a native PDF runtime.
  - Includes report metadata, metrics, report tables, chart source values, and warnings.
- Windows Report Center now exposes Preview, Excel, PDF, CSV, and HTML as first-class actions.
- Windows Report Center received a branded report-center header and clearer action styling.
- Windows Reports menu is reorganized into clearer categories: Overview & KPIs, Sales & Purchases, Employee & Credit, and Audit & Reconciliation, with report descriptions available as tooltips.
- Linux Reports menu uses the same category structure and gives every report Preview, Export to Excel, Export to PDF, and Export to CSV actions.
- Excel/PDF export uses the same `ReportDocument` used by preview/CSV/HTML; no report calculation is duplicated.

## Verification

- Existing static audit passes.
- v81 static checks verify that XLSX/PDF exporters are registered and both Windows and Linux expose the new formats.
- `dotnet build` / `dotnet test` could not be executed in this environment because the .NET SDK is unavailable.
