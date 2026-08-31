# Aegis.Cafeteria v74

v74 hardens the Services boundary introduced in v73 and implements production-critical management ownership rules.

## Implemented

- Management Additional Credit/Stipend endpoints no longer accept `ApprovedBy` or `ProcessedBy`; actor identity is derived from authenticated Windows/AD Negotiate credentials.
- Management operations require membership in configured `ManagementApi:AuthorizedGroups`.
- Replaced the v73 global Additional Credit cap with management-owned per-employee `CustomerCreditPolicies.AdditionalCreditLimit`.
- Additional Credit uses the existing shared `AdditionalCreditService` domain rule and preserves a supplied `OperationId` for idempotency.
- Added management endpoints to read/update each employee's Additional Credit policy.
- Additional Credit events carry the employee-specific limit so the POS can immediately recalculate the local account after event sync.
- Services now durably publishes service-originated Additional Credit, Stipend Card Reduction and Monthly Allocation events directly to FFP; POS availability is no longer required.
- Added retry/publication state (`FfpPublishedAt`, attempt count, last attempt/error) to service events.
- POS management-event synchronization no longer creates an FFP projection for management-originated events.
- When `UseServiceMonthlyAllocations=true`, POS FFP recovery does not recreate the monthly allocation projection; Services owns it.
- Monthly allocation remains calculated by the shared `MonthlyAllocationCalculator` using FFP00.Price for Credit Limit and FFP00.Cost for Subsidy.

## Development database note

The Services SQLite schema changes in v74. Archive/delete a development v73 `cafeteria.services.db` before the first v74 Services run. The POS SQLite schema remains version 73 because these changes do not alter POS persistence schema.

## Verification

- Added unit coverage for preserving an explicit Additional Credit OperationId.
- Added contract coverage that Additional Credit synchronization carries the employee-specific limit.
- Updated the static audit with v74 guards for server-derived management identities, per-employee credit policy and service-owned FFP publication.
