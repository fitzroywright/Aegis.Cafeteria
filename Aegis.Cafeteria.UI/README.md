# Windows POS — v47

The Windows POS uses local SQLite for offline operation and Cafeteria Services as its external-system boundary. The POS does not contain FFP transaction mappings or direct FFP database configuration. Startup refreshes cafeteria reference data from Cafeteria Services when available, then authenticates the operator through Common.Security. Normal sales record cashier and terminal. Refunds/voids require manager authorization.

With a second monitor, the customer display opens borderless/maximized on that monitor and shows customer profile/account data plus the previous calendar month's transaction history. It never displays employee number/TRN/NFC/barcode identifiers.


## Reports

Use the **Reports** menu to open the Report Center or launch a specific report. Reports can be previewed in the default browser or saved as HTML/CSV/Excel/PDF. See `../../docs/REPORTING.md` for the complete catalog.

## v52 recent transactions
The Windows POS shows today's recent completed terminal transactions. `Ui:RecentTransactionCount` defaults to 10 and can be set to 0 to hide the panel. The panel is not cleared by the customer inactivity privacy timeout.


### Recent transactions visibility (v54)
`Recent Transactions — Today` is shown only while no customer is selected. Selecting an employee or Cash Customer hides/collapses it for the active sale. It returns after **New Customer** or the inactivity timeout clears the customer.


## v56 idle workspace

The idle Windows POS uses a 50/50 split: Today's Sales Dashboard on the left and Recent Transactions — Today on the right. The original transaction workspace is restored when a customer is selected. Pressing Find with an empty lookup opens the customer browser; no separate customer-browse button is displayed.

Press **Esc** during an uncompleted transaction to abandon the customer/cart and return to the idle dashboard. Escape does not complete, void, refund, or publish the sale. Escape while idle does nothing.


### v58 customer identification
Customer Search is universal: NFC -> Code -> TRN -> Name. The lookup-type dropdown is removed. While idle the customer field is focused so keyboard-wedge NFC readers can identify a customer without cashier mode selection. Empty Find opens/browses employees. Only NFC, Code and TRN are cached as identifiers; Name is the directory-search fallback.

## Administration parity

Windows and Linux both provide price maintenance and employee email preferences administration through Cafeteria Services. FFP transaction mappings, legacy imports, and direct FFP database configuration are not POS responsibilities.

## Configuration ownership

POS configuration is limited to terminal-local concerns: local SQLite and terminal identity, UI behavior, local health/logging, manager authorization timeout, deployment/control-plane identity, and Cafeteria Services connectivity. External-system database settings, transaction mappings, publication batching/retry/poison policy, month-opening policy, imports, and scheduled messaging belong to Cafeteria Services or Cafeteria Management and must not be added to POS configuration.

## Business-rule boundary

The POS UI must not own cafeteria business rules. Tender eligibility/allocation, customer responsibility, refund eligibility/allocation, customer-payment eligibility, manager enforcement for product/price maintenance, and weekly-summary prompt decisions are shared Application/Domain policies. The UI may parse input, display policy results, choose colors/layout, request confirmation or credentials, and show validation messages, but it must not duplicate or replace those rules.

## Presentation boundary

Cross-platform display semantics come from `Aegis.Cafeteria.Presentation`. Currency/date/time formatting and shared cafeteria terminology must be added there when Windows and Linux should render the same meaning. Framework-specific controls, colors, fonts, layout and interaction remain in this UI project.
