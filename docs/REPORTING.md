# Reporting — v88

The reporting layer uses one `ReportDocument` model for Preview, Excel, PDF, CSV and HTML so calculations reconcile across formats.

## Key reports

The report catalog includes executive/daily operational views, product sales, employee/account reports, refund/void audits, Additional Credit audit, FFP reconciliation and the v77 purchase reports.

### Itemized Purchases by Customer

Shows purchase date/time, customer, department, item, unit price, quantity, total, Cash portion and Card/Account portion for the selected period.

### Purchases Summarized by Person

Shows period, employee, department, Cash spend, Card/Account spend and Total spend.

Both support Day/Week/Month grouping with totals by person, department and period.

## Split tender

For item-level reporting, a split-tender order allocates cash/account value proportionally across the order's item value so the detailed report reconciles to the recorded tender.

## Zero-only rows

Rows whose measurable numeric values are all zero are suppressed centrally. Text/audit-only rows are retained.

## Exports

- XLSX — standards-compliant workbook, no Excel automation required.
- PDF — dependency-free printable report.
- CSV
- HTML

Report rendering is performed off the UI thread to avoid freezing the POS during large exports.

## Daily dashboard

Idle dashboard includes product pie charts for Items Sold — Quantity and Items Sold — Value, using top-five slices plus Other.
