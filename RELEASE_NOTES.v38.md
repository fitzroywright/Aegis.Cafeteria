# Aegis.Cafeteria v38

## Customer-facing second monitor

- Windows POS now detects a secondary Windows display at startup.
- When a customer is identified, a dedicated borderless customer-facing window is shown on the secondary monitor.
- The customer display shows the customer's name, photo, eligibility status, current available cafeteria balance summary, and previous calendar month's transactions.
- Previous-month transaction history is loaded through a new read-only `ICustomerAccountQuery.GetTransactionsAsync` API. It does not create or recalculate historical monthly allocations.
- Private identifiers (employee number, TRN, NFC tag and barcode) are deliberately not shown on the customer-facing display.
- If no second monitor is connected, the POS continues normally with the customer-facing display disabled.
- Clearing the current customer hides and clears the customer-facing window.
- Stale-query protection prevents a previous customer's history from appearing if another customer is identified while the history query is still running.

## Verification

Static/source verification and archive integrity checks were completed in the generation environment. A .NET 10 SDK is not available in that environment, so `dotnet build` and `dotnet test` remain required release gates on the target Windows development machine.
