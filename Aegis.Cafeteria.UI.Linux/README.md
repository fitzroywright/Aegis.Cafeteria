# Linux POS — v52

The Linux/Avalonia POS uses the same local offline persistence, Cafeteria Services integration boundary, and Common.Security operator authentication as Windows. FFP transaction mappings and direct FFP database configuration are service-side concerns, not POS concerns.

## Current parity baseline
- Cafeteria theme and maximized window.
- Common.Security operator sign-in before POS use.
- Cashier and terminal audit recorded on completed sales.
- Card/code/private-ID identification and live employee-name search.
- Cash Customer.
- Current-month customer details and transactions.
- Products, cart, cafeteria-account/cash tender and checkout.
- New Customer, Manager Void and selected completed-sale Refund with manager authorization/reason.
- Configurable inactivity privacy clear.
- Reports menu using the shared 13-report catalog; report preview opens as HTML; reports can also be exported to Excel, PDF, or CSV.
- Recent Transactions - Today panel from local SQLite.
- Employee email preferences administration through Cafeteria Services.
- Close confirmation.

## Configuration
```json
"Ui": {
  "CustomerInactivityTimeoutSeconds": 30,
  "RecentTransactionCount": 10
}
```
Set `RecentTransactionCount` to `0` to hide the dashboard list.

## Deliberate remaining differences
Linux now includes the same Report Center workflow as Windows: report selection, date range, employee/product filters, grouping, voided-order option, preview, and HTML/CSV/Excel/PDF export. Linux also includes the dedicated second-monitor customer display; the shared transaction engine and schema remain unchanged.

No central Aegis database or SignalR service is required. Local SQLite schema remains version 48.


### Recent transactions visibility (v54)
`Recent Transactions — Today` is shown only while no customer is selected. Selecting an employee or Cash Customer hides/collapses it for the active sale. It returns after **New Customer** or the inactivity timeout clears the customer.


## v56 customer browse interaction

Pressing Find with an empty lookup switches to Name mode and lists up to 50 cached employees for selection, matching the Windows no-extra-browse-button workflow as closely as the current Avalonia layout permits.

Press **Esc** during an uncompleted transaction to abandon the customer/cart and return to idle mode. Escape does not complete, void, refund, or publish the sale. Escape while idle does nothing.


### v58 customer identification
Customer Search is universal: NFC -> Code -> TRN -> Name. The lookup-type dropdown is removed. While idle the customer field is focused so keyboard-wedge NFC readers can identify a customer without cashier mode selection. Empty Find opens/browses employees. Only NFC, Code and TRN are cached as identifiers; Name is the directory-search fallback.

## Configuration ownership

POS configuration is limited to terminal-local concerns: local SQLite and terminal identity, UI behavior, local health/logging, manager authorization timeout, deployment/control-plane identity, and Cafeteria Services connectivity. External-system database settings, transaction mappings, publication batching/retry/poison policy, month-opening policy, imports, and scheduled messaging belong to Cafeteria Services or Cafeteria Management and must not be added to POS configuration.

The Linux idle dashboard now matches the Windows layout with daily KPI cards, a Monday–Friday sales-value chart, items-sold-by-value chart, ready-for-customer guidance, and a direct Daily Sales Report action.

## Business-rule boundary

The POS UI must not own cafeteria business rules. Tender eligibility/allocation, customer responsibility, refund eligibility/allocation, customer-payment eligibility, manager enforcement for product/price maintenance, and weekly-summary prompt decisions are shared Application/Domain policies. The UI may parse input, display policy results, choose colors/layout, request confirmation or credentials, and show validation messages, but it must not duplicate or replace those rules.
