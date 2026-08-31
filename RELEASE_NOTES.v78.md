# Aegis.Cafeteria v78

## Daily dashboard product pies

The Windows idle Daily Dashboard now shows two product composition pie charts for the current local day:

- **Items Sold — Quantity**: share of units sold by product.
- **Items Sold — Value**: share of gross item sales value by product.

The five largest categories are shown individually and all remaining categories are combined as **Other** for readability. The dashboard continues to show Net Sales, Transactions, Refunds and the recent-transaction panel.

## Customer-facing Available to Spend

The second-monitor customer display already received the current account value; v78 changes the ambiguous `AVAILABLE` caption to the explicit **AVAILABLE TO SPEND** label so the customer can see how much cafeteria-account purchasing capacity remains.

## Cafeteria Account / pay-later clarity

The Windows Payment dialog now displays:

- **Available to Spend** before the sale;
- the Cafeteria Account amount being charged;
- a live **New Customer Responsibility** amount;
- cash received, remaining amount and change due.

The explanatory text now makes the intended financial semantics visible: Cafeteria Account is the charge-account/pay-later tender. The portion covered by remaining company subsidy is company responsibility. Any Cafeteria Account amount above remaining subsidy becomes customer responsibility for later payment/payroll deduction.

Checkout remains conservative: Cafeteria Account cannot exceed Available to Spend, and Cafeteria Account + cash must fully cover the order. There is no persisted "unpaid tender" sale.

## Debt semantics confirmed

Current-month customer responsibility is not immediately classified as carried debt. At account-period close it becomes the expected payroll deduction after recorded customer payments. After payroll reconciliation, only the uncollected amount is converted to `CarriedForwardDebt` and applied to the following month, reducing the following month's spending capacity.

A voluntary later `CustomerPayment` domain/application service exists, but the Windows POS does not yet expose a cashier UI for recording that later payment. Payroll deduction remains the currently visible settlement route.
