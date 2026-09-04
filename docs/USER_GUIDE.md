# User Guide

## POS

### Starting the POS
Sign in with your authorized network account. The splash screen remains visible while the POS establishes its connection to Cafeteria Services. After startup completes, the main POS window should receive focus.

### Finding an employee
Search by NFC/tag, employee code, TRN, or name. Exact identifiers may load directly; name searches can return a selectable list.

### Completing a sale
Select the employee, add products, review the transaction, and proceed to tender. Employee Credit is never automatically selected. The cashier must ask the employee and explicitly choose it. If the employee declines and the remaining tender is insufficient, the sale cannot be completed until the tender or transaction is changed.

### Outstanding Balance
Outstanding Balance represents what remains payable outside payroll and therefore excludes payroll-deduction amounts.

### Customer payments
A cash payment against an employee balance appears as a Payment transaction. Amount Due is the outstanding balance before payment, From Card is blank, Cash Payment is the cash received, and Balance is the new outstanding balance.

### Refunds
The Refund Selected grid shows purchase rows only. Payments are not refundable purchases and are excluded from the refund chooser.

### Escape / cancel
Escape cancels the current transaction and returns to idle behavior as designed. Redundant Void Current Sale explanatory text is intentionally absent.

## Customer-facing display

The secondary/customer screen shows customer identity, transaction information, and recent activity. Payment rows are displayed using customer-friendly amounts rather than raw internal accounting signs.

## Management Console

### Employee Search
Find employees by name, code, or TRN. Selecting a result makes that employee the target for employee-specific management actions.

### Additional Credit
Load or save the employee-specific monthly additional credit limit and approve an additional-credit amount with a reason.

### Process Stipend
Record stipend cash, card reduction, payment mode, expense-report reference, and notes.

### Rates
Load or change the daily credit-limit rate and daily company-subsidy rate used by month opening.

### Month Opening
Run normal month opening or recalculate a selected month. Month closing is not exposed in this release.

### Employee Amount Due
Load the payroll-facing amount-due report for a year/month. Send Report emails the report. Force resend overrides duplicate-send protection when an intentional resend is required.

### Email & Weekly Reports
Enable/disable the weekly manager summary, configure manager email, day and hour, load/save settings, and send a test email.

### Audit Trail
Search the audit history by actor, action, customer ID, success/failure, date/time range, and result count.

### Diagnostics
Open Engineering Diagnostics for technical health and troubleshooting information.

### Service Status
Shows whether the Management Console can communicate with Cafeteria Services.

### Sign Out
Ends the Management session.
