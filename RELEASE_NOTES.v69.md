# Aegis.Cafeteria v69

## Canteen Summary report

Adds a first-class **Canteen Summary** report modeled on the existing FFP LunchTicketSummary report.

For the selected date range the report shows, grouped by FFP department:

- Employee Name
- Amt Used - net cafeteria-account tender in the selected range (account refunds reduce usage)
- Work Days - weekdays in the selected range excluding cached FFP holidays
- FFP Pmt - configured daily subsidy rate (`SubsidyCost`, default `FFP00.Cost`)
- FFP Amt - Work Days x FFP Pmt
- Deduction - max(0, Amt Used - FFP Amt)

The printable HTML layout follows the supplied FFP report with organization heading, date range, department bands, repeated table header, totals, and print timestamp. Browser preview can be printed or saved as PDF.

## Department reference data

FFP employee refresh now joins `Employees.DepartmentID` to `Departments.ID` and caches the department name locally so the report remains available offline after reference refresh.

This adds `Customers.Department` and bumps the greenfield local SQLite schema to **69**. During development, delete/archive the prior local database before first v69 run.

## Preserved behavior

- Credit Limit rate remains the configured policy row `Price` (default `FFP00.Price`).
- Subsidy rate remains the configured policy row `Cost` (default `FFP00.Cost`).
- The identifier normalization null/empty guard remains in place before identifier database lookup.
- v68 refund selector behavior (last five refundable transactions) remains unchanged.
