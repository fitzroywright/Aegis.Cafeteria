# Aegis.Cafeteria v79

## Customer Payment workflow completed

Windows and Linux now expose **Record Payment** for an eligible cafeteria-credit customer with an outstanding `PayrollDeductionRequired` balance.

The cashier records:

- payment amount;
- payment method (`Cash`, `Card / POS`, `Bank Transfer`, `Cheque`, or `Other`);
- optional receipt/POS/bank reference;
- receiving operator (captured from the authenticated session);
- payment date/time.

The shared `CustomerPaymentWorkflow` validates that the period is open, the customer has a cafeteria-credit account, the payment is positive, and the payment does not exceed the current amount due. It persists the payment locally first. When external ledger publication is enabled it queues an idempotent `CustomerPayment` ledger adjustment using the payment ID as the operation ID.

A customer payment reduces outstanding customer responsibility/payroll deduction and re-opens the same amount of cafeteria spending capacity, capped by the customer's approved total credit limit.

## Windows / Linux parity

v79 brings the recent POS behavior into parity on Linux:

- **Record Payment** with the same validation, payment methods, reference and operator capture as Windows;
- checkout shows a live **New Customer Responsibility** value and explains that Cafeteria Account is the pay-later/charge-account tender;
- Linux refunds now support selected items/quantities and split-tender-safe partial refunds instead of forcing `RefundAllRemaining`;
- the Linux customer-facing second monitor explicitly shows **AVAILABLE TO SPEND** and current-month transactions;
- the Linux idle Daily Dashboard now includes **Items Sold — Quantity** and **Items Sold — Value** pie charts using the same current-day report data and top-five-plus-Other grouping as Windows.

## Audit history

`CustomerPayment` persistence now retains `PaymentMethod` and `ReceivedByUserId`. The customer transaction history describes the payment method, optional reference and receiving operator.

## Verification

`python scripts/static-audit.py` passes, including new v79 regression guards for customer-payment persistence/FFP publication and Windows/Linux parity.

`dotnet clean`, `dotnet restore`, `dotnet build` and `dotnet test` were not run because the .NET SDK is not installed in this execution environment. Run the normal Release build/test gate before deployment.
