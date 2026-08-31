# Aegis.Cafeteria v57

## POS Escape-to-cancel

- Windows and Linux POS now treat **Escape** as a cancel shortcut only while a customer/transaction is active.
- Escape abandons the current in-memory customer/cart state and returns the POS to idle mode.
- No order is completed or persisted by the Escape path.
- No void, refund, manager action, external FFP ledger projection, or other financial event is created.
- Windows uses form-level `KeyPreview` so Escape works when focus is inside ordinary POS controls.
- Escape while already idle is intentionally a no-op.

This release does not change the SQLite schema or any funding/checkout calculation.
