# Aegis.Cafeteria v52

## Recent transactions and Linux parity

- Added a shared read-only `IRecentTransactionQuery` backed by local SQLite.
- Windows POS now shows the most recent completed transactions for the current day directly in the main UI.
- Recent rows show time, customer, total, tender summary and cashier; private employee identifiers are never shown.
- `Ui:RecentTransactionCount` controls the number of rows (default 10, 0 hides the panel).
- Recent activity refreshes at startup and immediately after completed sales/refunds; Linux also refreshes it on the periodic status timer.
- Recent terminal activity is independent of the selected customer and remains visible when the customer privacy timeout clears Customer Details.

### Linux UI parity

- Linux now uses the same navy/white cafeteria branding and maximized main-window layout as Windows.
- Added themed operator sign-in using the same visual language as the Windows splash/login experience.
- Added operator session, manager authorization, Manager Void and refund wiring through the same application services used by Windows.
- Added Reports menu with the shared 13-report catalog; Linux previews reports as HTML in the default browser using month-to-date defaults.
- Added live employee-name search with debounce.
- Added configurable customer inactivity clearing using `Ui:CustomerInactivityTimeoutSeconds`.
- Added Recent Transactions — Today panel using the same SQLite read model as Windows.
- Added New Customer terminology and close confirmation.

No database schema change. SQLite schema remains version 48. No checkout, funding, FFP projection, or persistence-domain rules were changed.
