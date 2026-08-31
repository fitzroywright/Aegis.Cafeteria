# Aegis.Cafeteria v77

## Purchase reporting

Added two first-class purchase reports to the shared reporting layer and Windows/Linux report menus:

1. **Itemized Purchases by Customer**
   - selectable date range;
   - purchase date/time;
   - customer name;
   - department;
   - item name;
   - unit price;
   - quantity;
   - full item total;
   - separate Cash and Card/Account amounts;
   - tender label (Cash, Card/Account, or Split).

2. **Purchases Summarized by Person**
   - selectable date range;
   - selected Day/Week/Month grouping;
   - department;
   - customer;
   - separate Cash and Card/Account spend;
   - total spend.

Both reports include totals grouped at three levels: period + department + person, period + department, and period overall. Week grouping is Monday-Sunday.

Split-tender orders are allocated proportionally across item values for reporting only, with currency-rounding remainder applied to the final line so the item tender allocations reconcile to recorded tender. This does not alter persisted orders, payments, refunds, balances, FFP publishing, or accounting state.

Cash Customer purchases are included. If no department is available they appear under `Unassigned`. Refunds remain available through the Refund and Void Audit and are not converted into negative purchase lines.
