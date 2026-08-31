# Aegis.Cafeteria v80

- Employee summary now displays **Payroll Deduction** on both Windows and Linux.
- The value is sourced directly from `FinancialStatus.PayrollDeductionRequired`; no duplicate calculation was introduced.
- Payroll Deduction refreshes whenever the customer account view is refreshed, including after purchases, refunds and customer payments.
- Cash/ineligible customers show no payroll deduction value, consistent with other credit-only financial fields.
- Windows/Linux parity retained for this customer-summary field.
