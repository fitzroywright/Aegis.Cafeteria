# Aegis.Cafeteria v83

## Customer payroll visibility

- Customer-facing displays on Windows and Linux now show **PAYROLL DEDUCTION** using the shared `PayrollDeductionRequired` value.
- Cash/ineligible customers show an em dash rather than a payroll value.
- The operator customer summary now distinguishes:
  - **Customer Responsibility (before payments)** — gross amount owed before voluntary customer payments.
  - **Customer Payments** — payments already received in the current period.
  - **Payroll Deduction** — the remaining amount expected to be taken through payroll.
- This is a presentation clarification only; the account calculation is unchanged.

## Retryable customer search

- Fixed the Windows candidate/results dialog so the original search text is no longer read-only.
- The dialog now retains `ICustomerIdentificationService` when opened from universal Find results.
- Cashiers can edit the term in-place and the grid re-searches automatically after the existing debounce; Enter also executes the search immediately.
- The dialog no longer has to be closed and reopened to correct or broaden a name search.
- Existing live-search behavior is preserved; no redundant Search button is introduced.

## Parity and verification

- Payroll Deduction remains visible in the Windows and Linux operator summaries and is now also present on both customer-facing displays.
- Added v83 static regression checks for payroll visibility, clearer responsibility/payment wording, and retryable Windows search.
- Static audit passes.
- `.NET` SDK is not available in the packaging environment, so Release build/unit tests must still be run on the build machine.
