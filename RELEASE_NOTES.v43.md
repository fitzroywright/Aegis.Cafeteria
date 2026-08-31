# Aegis.Cafeteria v43

v43 is a corrective release for the v42 greenfield baseline. It does not change the SQLite schema; schema version 42 remains the current greenfield database schema.

## Fixed

- Removed the obsolete `CustomerAccountService.GetStatement` overload that referenced a calculator signature removed during the v42 cleanup.
- Updated every `CustomerAccountCalculator.Calculate` call site to supply the required `stipends` collection.
- Converted the production calculator call sites and affected tests to named arguments so future collection parameters cannot be shifted accidentally.
- Updated statement/calculator tests that still used the pre-stipend positional signature.
- Added `CustomerAccountCalculatorContractTests` to lock the single canonical calculator signature and explicitly verify that `stipends` is required while prior-period refund credits remain optional.

## Database

No schema change. Fresh installations continue to create `LocalSchemaInfo.SchemaVersion = 42` / product schema `v42`.
