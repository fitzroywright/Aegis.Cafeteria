# Aegis.Cafeteria v49

## Customer details current-month transaction history

- Corrected the customer-facing details display to show transactions for the **current cafeteria calendar month** rather than the previous month.
- The history query now uses `CustomerAccountView.Period.Year` and `.Month` directly.
- Updated the customer-display labels to `Current month transactions` and `No transactions in the current month.`
- Removed the obsolete `CustomerDisplayPolicy.PreviousCalendarMonth` helper and its tests.
- Updated current documentation to describe current-month customer history.

No database schema change. The local SQLite schema remains version 48.
