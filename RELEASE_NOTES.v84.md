# Aegis.Cafeteria v84

- Clarified the current-sale void workflow on Windows and Linux. The action is now labelled **Void Current Sale**, and an inline state message explains exactly when it becomes available. Once the cart contains at least one item, the UI explicitly says that void is available and that manager approval plus a reason are required.
- Redesigned Customer Payment placement and wording on both platforms. The action now lives with the employee financial summary as **Customer Payment...**, with text explaining that it reduces Payroll Deduction.
- Redesigned the Customer Payment dialog/window around the payroll effect: Payroll Deduction before payment, payment amount/method/reference, and Payroll Deduction after payment. The confirmation action is **Apply Payment**.
- No void authorization rules, payment accounting rules, persistence, FFP publication, refund behavior, or checkout calculations changed.
- Windows/Linux parity retained for both workflows.
