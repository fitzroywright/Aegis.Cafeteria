# FFP Manager Integration for Cafeteria Management — v88

FFP Manager should not write Cafeteria financial state directly into `Employees`, `EmployeeIDScans`, or the POS SQLite database. Manager-side actions must call `Aegis.Cafeteria.Services`, which validates authorization, applies business rules, stores a durable management event and publishes the resulting compatibility projection to FFP.

## 1. Why the Services API is the boundary

The API provides four important guarantees:

1. The actor is derived from the authenticated Windows/AD principal; FFP Manager cannot spoof `ApprovedBy` or `ProcessedBy`.
2. Per-employee policy and cumulative approval limits are enforced centrally.
3. Each operation can carry a GUID `OperationId` for idempotent retries.
4. FFP publication happens independently of whether a POS terminal is online.

## 2. Authentication from FFP Manager

Management endpoints use Windows Integrated Authentication (Negotiate), not the POS API key.

FFP Manager should call Services using the logged-in Windows user credentials. For .NET Framework code this normally means an `HttpClientHandler`/`WebRequestHandler` configured to use default Windows credentials. Do not send a manager username in the JSON payload.

The authenticated user must belong to one of:

- `Cafeteria Managers`
- `Cafeteria Administrators`
- `Domain Admins`

The exact list is configurable in Services `ManagementApi:AuthorizedGroups`.

## 3. FFP Manager UI additions

Recommended Cafeteria section in the Employee detail view:

### Cafeteria status panel

Read-only display:

- Eligible for Cafeteria Credit
- Current FFP `CreditLimit`
- Current FFP `AvailableBalance`
- configured Additional Credit Limit
- Additional Credit approved this month
- Employee Amount Due / responsibility data if exposed by the Cafeteria reporting/API layer in a later management endpoint

### Actions

- **Set Additional Credit Limit**
- **Approve Additional Credit**
- **Record Stipend / Meal Advance**
- **Run Cafeteria Month Opening** — administrative screen, not per employee
- **Open Cafeteria Audit/Reports** — either API/report integration or a link to the Cafeteria reporting surface

Do not add a direct `CreditLimit` or `AvailableBalance` editor. Those fields are compatibility projections maintained by Cafeteria.

## 4. Set Additional Credit Limit

### GET policy

`GET /api/management/customers/{customerId}/credit-policy`

Response contains employee name and current `AdditionalCreditLimit`.

### PUT policy

`PUT /api/management/customers/{customerId}/credit-policy`

Body:

```json
{
  "additionalCreditLimit": 5000.00
}
```

Rules:

- limit cannot be negative;
- employee must exist in FFP;
- `UpdatedBy` is taken from Windows authentication;
- this is a policy ceiling, not an immediate credit grant.

FFP Manager should expose this as an administrator/manager-controlled employee setting. A value of zero means no Additional Credit may be approved.

## 5. Approve Additional Credit

`POST /api/management/additional-credit`

Body:

```json
{
  "customerId": 123,
  "amount": 1500.00,
  "reason": "Approved for field assignment meals",
  "operationId": "<new-guid>"
}
```

`approvedAt` is optional; normally omit it and let Services use server local time.

### FFP Manager workflow

1. User opens employee.
2. FFP Manager GETs the employee credit policy.
3. Show Additional Credit Limit and, ideally, approved-to-date/remaining capacity.
4. User clicks **Approve Additional Credit**.
5. Require positive Amount and meaningful Reason.
6. Generate a GUID once when the user submits; keep the same GUID if the HTTP request must be retried.
7. POST to Services using Windows credentials.
8. On 201/200, refresh the displayed cafeteria balances/status.
9. On 400, show the business-rule message.
10. On 409, tell the manager that an employee-specific Additional Credit Limit must first be configured.
11. On 401/403, show an authorization failure and do not retry as another identity.

### Business rule

For the month:

`sum(existing approvals) + requested amount <= employee Additional Credit Limit`

Additional Credit increases total spending capacity and the compatibility `CreditLimit`/`AvailableBalance`, but does not increase company subsidy.

## 6. Stipend / meal advance

`POST /api/management/stipends`

Example:

```json
{
  "customerId": 123,
  "cashAmount": 2000.00,
  "cardReductionAmount": 1500.00,
  "paymentMode": "Advance",
  "expenseReportReference": "EXP-2026-00123",
  "notes": "Three-day field assignment",
  "operationId": "<new-guid>"
}
```

`paymentMode` is `Advance` or `Reimbursement`.

Rules:

- CashAmount > 0.
- CardReductionAmount >= 0.
- CardReductionAmount <= CashAmount.
- Employee must exist and be cafeteria-credit eligible.
- ProcessedBy is derived from authenticated Windows user.
- Cash-only stipend does not change the Cafeteria balance.
- CardReductionAmount reduces FFP AvailableBalance but not CreditLimit.

FFP Manager should keep cash amount and cafeteria-card reduction as separate fields. Do not infer that the entire cash stipend must reduce the cafeteria account.

## 7. Manual month opening

`POST /api/month-opening/run?year=2026&month=9`

Use an administrative Cafeteria screen with explicit year/month and a confirmation dialog.

Services calculates working days from FFP Holidays and calculates funding from the configured LunchItems policy rows. Events use deterministic IDs, making a rerun safe.

FFP Manager should display the returned `created` count and then instruct the user to review the Cafeteria allocation/audit report.

## 8. Employee lookup/data source

FFP Manager already has the Employee ID. Send the integer FFP `Employees.ID` as `CustomerId`. Do not send Code/TRN/NFC as the management API identity.

Services independently re-reads the employee from FFP and validates eligibility before financial actions.

## 9. Error handling contract

Treat status codes as follows:

- 200/201 — success/idempotent existing operation.
- 400 — validation/business rule; display returned message.
- 401 — Windows authentication not established.
- 403 — authenticated user lacks Cafeteria management role.
- 404 — employee not found in FFP.
- 409 — prerequisite policy missing, such as Additional Credit Limit.
- 5xx — service/integration failure; preserve the original OperationId and offer retry.

Never generate a new OperationId simply because a request timed out after submission; doing so defeats idempotency.

## 10. Suggested FFP Manager implementation structure

For the existing FFP Manager/XAF codebase, keep the Cafeteria integration isolated rather than embedding HTTP calls in business objects.

Suggested components:

- `ICafeteriaManagementClient`
- `CafeteriaManagementClient`
- DTOs mirroring the management API requests/responses
- Employee Detail View controller/actions
- Cafeteria Administration controller for month opening
- authorization visibility based on the same AD groups, while still relying on Services as the final authorization authority
- centralized error translation/logging

Do not duplicate Additional Credit limit calculations in FFP Manager. The UI may display expected remaining capacity, but Services remains authoritative.

## 11. Recommended user experience

### Employee view

Cafeteria group:

```text
Eligibility                 Eligible
Additional Credit Limit     $5,000.00
Approved This Month         $1,500.00
Remaining Approval Capacity $3,500.00

[Set Credit Limit] [Approve Additional Credit] [Record Stipend]
```

### Approve Additional Credit dialog

```text
Employee: Fitzroy Example
Additional Credit Limit:     $5,000.00
Already Approved:            $1,500.00
Remaining Capacity:          $3,500.00
Amount:                      [          ]
Reason:                      [                          ]

[Approve] [Cancel]
```

After success show the authenticated approver, amount, timestamp and operation reference.

## 12. What FFP Manager must stop doing

Do not directly:

- increment `Employees.CreditLimit`;
- increment/decrement `Employees.AvailableBalance` for Cafeteria management operations;
- insert Additional Credit/Stipend/month-allocation `EmployeeIDScans` rows;
- write POS SQLite data;
- supply `ApprovedBy`/`ProcessedBy` strings;
- calculate month-opening entitlement independently.

All of those responsibilities belong to Cafeteria Services.

## 13. Acceptance tests for the FFP Manager changes

- Authorized manager can read and change an employee Additional Credit Limit.
- Unauthorized user sees no action or receives 403 if attempting it directly.
- Additional Credit within remaining cap succeeds.
- Additional Credit above cap is rejected.
- Retrying the same OperationId does not duplicate the approval.
- Employee with no configured limit receives the prerequisite message.
- Ineligible employee cannot receive Additional Credit/Stipend adjustment.
- Stipend cash and card reduction remain distinct.
- Month opening rerun does not duplicate allocations.
- FFP `EmployeeIDScans`, `CreditLimit`, `AvailableBalance` and POS account view reconcile after synchronization.


## Funding policy / FFP00 maintenance

FFP Manager is the primary administrative UI for Cafeteria funding policy. The POS is not required and does not expose funding-policy editing.

Read the current policy:

`GET /api/management/funding-policy`

Update it:

`PUT /api/management/funding-policy`

```json
{
  "dailyCreditLimitRate": 500.00,
  "dailyCompanySubsidyRate": 300.00
}
```

Services first saves the authoritative audited funding policy in its own database and then mirrors the same values to the FFP `LunchItems` row whose `Code = FFP00`:

- `FFP00.Price = DailyCreditLimitRate`
- `FFP00.Cost = DailyCompanySubsidyRate`

This compatibility mirror allows legacy FFP reports/tools to continue seeing the familiar FFP00 values, but month opening and Aegis calculations use the Services policy rather than re-reading FFP00.

FFP Manager should expose these values under a Cafeteria Administration screen and call Services using the authenticated Windows identity. It must not update FFP00 directly because doing so would bypass the Services audit history and could leave Services and FFP out of sync.

## Employee Amount Due / payroll ownership (v95)

FFP Manager or the payroll system owns payroll processing. Cafeteria exposes/calculates Employee Amount Due but does not track payroll deductions or collections. If FFP Manager needs a stable period-end amount, it should consume the closed-period Employee Amount Due and handle all deduction, collection and reconciliation states outside the POS.
