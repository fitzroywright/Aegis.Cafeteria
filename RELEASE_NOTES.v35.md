# Aegis.Cafeteria v35

## POS payment and startup visibility

- Retains the Food For The Poor Jamaica startup splash from v34, with live startup-stage messages for configuration, local storage, FFP employees, holidays, subsidy policy, products, and terminal services.
- Product IDs remain internal only and are not displayed in Windows or Linux product/catalog/cart lists.
- Windows payment dialog now requires the cashier to enter **Cash Received** rather than auto-calculating cash.
- Payment dialog displays **Remaining** and **Change Due** in real time.
- A sale cannot complete while any amount remains unpaid.
- Cash overpayment is supported: only the cash needed to settle the sale is recorded as tender; the excess is displayed as change due.
- Linux POS payment panel follows the same rules and no longer pre-fills the required cash amount.
- Cafeteria-account tender is bounded by available credit; cash customers and ineligible employees have no cafeteria-account tender.

Example: a $1,000 order with $500 cafeteria account + $300 cash received shows $200 remaining and cannot complete. With $500 cafeteria account + $600 cash received, $500 cash is applied and $100 change is due.
