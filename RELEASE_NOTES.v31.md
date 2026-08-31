# Aegis.Cafeteria v31

## Operator privacy
- Customer-facing/operator-facing DTOs no longer expose TRN, NFC or barcode values.
- Windows and Linux customer screens no longer display employee number, TRN, NFC, barcode or temporary identifiers.
- Customer directory is name-only and shows employee name, photo and cafeteria-credit eligibility.
- Secret identification modes remain functional internally but are labeled generically as Card, Code and Private ID.
- Private identifier entry is masked in both Windows and Linux POS lookup controls.
- Secret identifiers remain in the local indexed cache only for identification/integration purposes.

## Credit eligibility
- Employees who are not eligible for cafeteria credit do not receive a calculated monthly allocation.
- MonthlyCreditAllocationService bypasses the funding provider, allocation store and external allocation publication for ineligible employees.
- Customer account query does not run credit/subsidy/debt/payroll balance calculation for ineligible employees.
- Ineligible employees remain identifiable and may make cash purchases.
- UI clearly displays `Not eligible for cafeteria credit - cash purchases only` and shows credit-related values as unavailable rather than zero balances.

## Tests and cleanup
- Added unit coverage proving ineligible customers do not load funding or persist monthly allocations.
- Updated customer search tests for privacy-safe presentation DTOs.
- Removed obsolete presentation helper code that rendered private identifiers.

## Verification
- JSON and project XML parsing passed.
- Static brace/member/privacy scans passed.
- The generation environment does not include the .NET SDK, so `dotnet build` and `dotnet test` were not run here.
