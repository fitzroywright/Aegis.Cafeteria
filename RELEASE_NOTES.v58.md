# Aegis.Cafeteria v58

## Universal customer identification

- Removed the customer lookup-type dropdown from Windows and Linux POS UIs.
- The single Customer Search field now resolves input in this order: Card/NFC, FFP Employee Code, TRN, then employee Name.
- Empty Find opens/browses the cached employee directory.
- Name input retains lightweight live suggestions/search behavior without exposing private identifiers.
- The customer lookup field receives focus whenever the POS is idle: startup, New Customer, Escape cancel, completed-sale reset, and inactivity reset.
- Once a customer is loaded, focus moves to Product Search and the POS does not steal focus back during the transaction.
- FFP `Employees.Code` is represented explicitly as `CustomerIdentifierType.Code`; the previous `Barcode` enum name remains only as a compatibility alias for old persisted/test code.

## Stability

- No checkout, funding, reporting, FFP ledger publication, or SQLite schema changes.
- SQLite schema remains version 48.
