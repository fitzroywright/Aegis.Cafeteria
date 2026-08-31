# Aegis.Cafeteria v85

## Reporting cleanup

- Report data lines whose measurable numeric values are all zero are now suppressed.
- Rows that contain at least one non-zero numeric value remain visible.
- Text-only rows remain visible because they may carry meaningful audit/detail information.
- The rule is applied centrally when the `ReportDocument` is created, so Preview, HTML, CSV, Excel and PDF all show the same filtered rows.
- Totals and report calculations are unchanged; this is display/export cleanup only.
