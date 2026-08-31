# Aegis.Cafeteria v75

v75 fixes the Windows POS refund workflow so completed sales are no longer limited to an all-or-nothing refund from the UI.

## Refund UI
- Added `RefundDialog` for completed-sale refunds.
- Cashiers can select the refund quantity for each still-refundable order item.
- A **Refund All Remaining** shortcut preserves the previous full-refund workflow.
- The dialog shows the selected refund total and remaining original cafeteria-account/cash tender.
- Cafeteria-account allocation can be adjusted only within the original remaining tender; cash is derived so allocations always equal the selected item value.
- Existing manager authorization and mandatory reason remain unchanged.
- The POS now calls `RefundService.Refund(...)` with the selected items and tender allocation rather than forcing `RefundAllRemaining(...)`.
- Existing domain guards continue to prevent over-refunding item quantities, order value, cafeteria-account tender, or cash tender.
- Existing FFP publication remains unchanged: only the cafeteria-account portion is restored to FFP; cash remains a cash refund.

## Verification
- Repository static audit includes regression guards ensuring the Windows refund flow opens `RefundDialog`, uses `RefundService.Refund(...)`, and does not force `RefundAllRemaining(...)`.
- No POS database schema change is required; the local schema version remains 73.
- A .NET SDK was not available in the artifact-generation environment, so v75 could not be compiled or unit-tested here. Run the normal clean restore/build/test pipeline before deployment.
