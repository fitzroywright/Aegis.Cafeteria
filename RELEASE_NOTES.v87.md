# Aegis.Cafeteria v87

## POS layout and payment-entry usability

- Windows active and idle POS workspaces now use an even 50/50 left/right split so customer identity, funding, payroll and history information remain visible.
- Linux main POS workspace now uses the same 50/50 split.
- Windows grid column headers inherit the alignment of their data cells; currency/numeric headings align with right-aligned values, boolean headings align with centered values, and text remains left-aligned.
- Refund numeric columns and employee-search eligibility headers/cells use matching alignment.
- Windows sale payment recalculates Remaining, Change Due, new customer responsibility and Complete-state from the live NumericUpDown editor text on each keystroke. Cashiers no longer need to Tab out of the amount field before clicking Complete.
- Linux already recalculated tender state on TextChanged and remains behaviorally aligned.

No funding, payroll, tender, refund, or reporting calculation rules changed.

## Verification

- Static audit: PASS, including v87 layout/alignment/live-payment regression checks.
- .NET build/tests: NOT RUN in this execution environment because the .NET SDK is unavailable.
