# Aegis.Cafeteria v91

## Split tender

- Cash entry now automatically reduces the Cafeteria Account portion of a split tender.
- The automatic amount is capped by the employee's available cafeteria-account capacity.
- Required cash gaps remain visible when available credit is insufficient.
- Excess cash continues to produce Change Due.
- Windows and Linux use the same shared tender-allocation calculation.

## Refunds

- Refund reason dialogs now open with an editable factual default reason.
- The generated reason identifies selected item quantities/product names and the original sale time.
- No subjective or invented reason is generated.

## Tests and verification

- Added unit tests for tender auto-allocation and refund-reason generation.
- Added v91 static-audit guards for Windows/Linux parity.
- .NET build/test execution was not available in the packaging environment.
