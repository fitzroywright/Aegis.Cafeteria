# Aegis.Cafeteria v60

## Customer name search correctness

- Name search now uses contains matching, matching Product Search behavior.
- Exact NFC/Card, employee Code and TRN remain the only auto-load identification paths.
- Name matches never auto-load a customer, even when only one partial-name candidate is returned.
- Windows always opens the customer chooser for one or more name candidates.
- Linux continues to show the candidate list for explicit selection.
- No database schema or business-rule changes.
