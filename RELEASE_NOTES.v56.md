# Aegis.Cafeteria v56

## POS idle experience

- Removed the separate Windows `Customers` / `Browse Customers` controls.
- Pressing **Find** with an empty customer search/identifier box opens the existing customer browser immediately, regardless of the selected lookup mode.
- Linux mirrors the empty-Find behavior by switching to Name browse mode and listing the first 50 cached employees for selection.
- The Windows idle workspace now uses a true 50/50 main split:
  - left: **Today's Sales Dashboard** (Daily Sales report metrics for today),
  - right: **Recent Transactions — Today**.
- The dashboard shows Net Sales, Transactions, and Refunds and includes a shortcut to the full Daily Sales report.
- Selecting an employee or Cash Customer restores the original active-transaction split and original transaction workspace unchanged.
- Returning to idle restores the 50/50 dashboard/recent-transactions view.

## Stability

- No checkout, funding, FFP publication, order persistence, refund/void domain logic, or database schema changes.
- SQLite schema remains version 48.
