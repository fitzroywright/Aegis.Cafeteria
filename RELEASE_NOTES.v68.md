# Aegis.Cafeteria v68

## Refund chooser

- The Windows POS refund chooser now displays at most the last five completed sales that still have refundable value.
- The query still looks through up to 50 recent completed orders so fully-refunded transactions do not prevent older refundable transactions from appearing.
- The chooser title now explicitly says "Last 5 Refundable Customer Sales" (or Cash Sales).
- No SQLite schema change is required from v67.

## Funding and identification rules retained

- Credit Limit rate remains the configured FFP policy row's `Price` (default `FFP00.Price`).
- Subsidy rate remains the configured FFP policy row's `Cost` (default `FFP00.Cost`).
- The identifier normalization null/empty guard remains in place before identifier database queries.
