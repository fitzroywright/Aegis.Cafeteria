# Aegis.Cafeteria v32

## Fixes rolled forward
- Fixed the v31 `MonthlyCreditAllocationService` duplicate `days` local variable compile error.
- Fixed `FfpEmployeeRefreshIntegrationTests` to use the real `SqliteTestDatabase.CreatePath/Delete` helper instead of nonexistent `CreateAsync/Factory` members.
- Retains v30/v31 fixes for FFP ledger `EmployeeId`, messaging `EmployeeId`, privacy-safe UI, source-neutral funding, and removal of the legacy monthly rate schedule runtime path.

## FFP-backed product catalog
- Added source-neutral `IProductCatalogSource` / `IProductCatalogCache` boundaries.
- The current FFP SQL adapter reads active sale items from `dbo.LunchItems`.
- `Code='FFP00'` is explicitly excluded from products for sale and remains the company subsidy policy row.
- FFP product ID, name, and price are projected into the durable local SQLite Products cache.
- Product refresh runs at startup, on the background FFP refresh cycle, and with manual reference refresh.
- POS product browsing remains local-first/offline-capable.

## Cash customer preserved
- Added a first-class anonymous Cash Customer POS flow rather than a fake FFP employee.
- Cash Customer allows cash tender only, never loads funding, never creates monthly allocations, and never publishes employee ledger entries to FFP.
- Windows and Linux POS expose a Cash Customer action.

## Eligibility and privacy
- Ineligible employees bypass working-day/funding/allocation calculation entirely and remain cash-only.
- Employee number, TRN, NFC, barcode and temporary identifiers remain internal-only and are not shown to operators.
- Private identifier entry remains masked; employee directory search is name-only.

## Verification
- Added product-cache refresh coverage and cash-customer checkout coverage.
- Static source/configuration/package checks performed. The generation environment does not contain the .NET SDK, so `dotnet build` and `dotnet test` must be run on a .NET 10 development machine.
