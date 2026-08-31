# Release Notes v61

- Removed the developer-facing word `Poisoned` from Windows and Linux operator status displays. Failed FFP publications that have exceeded automatic retries are now shown as `Requires attention`.
- Corrected `FfpReferenceCacheTests.EmployeeRefresh_ReplacesIdentifiersAndEligibility_WithoutLegacyFundingState` to assert the normalized identifier projection used by lookup while preserving original FFP display values.
- Added checks for refreshed NFC, employee Code, and TRN identifiers.
- No database schema or business-rule changes.
