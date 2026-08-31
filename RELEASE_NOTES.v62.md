# v62

## Customer name Find-button selection fix

- Fixed the Windows customer chooser used by universal Customer Search / Find.
- Name-search candidate dialogs no longer preselect the first employee.
- `SelectedCustomer` now requires an explicitly selected grid row rather than accepting `CurrentRow`.
- The employee selection button remains disabled until the cashier explicitly selects a candidate.
- The text entered in Customer Search is carried into the candidate dialog so the operator can see which name query produced the list.
- Exact NFC/Card, Employee Code and TRN matches still load immediately.
- No checkout, FFP, funding, reporting or database schema changes.
