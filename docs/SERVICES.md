# Aegis.Cafeteria.Services — v88

## Responsibilities

Services is the recommended production integration boundary. It owns:

- centralized FFP SQL access;
- management API;
- employee Additional Credit policy;
- durable Additional Credit and Stipend events;
- month opening;
- direct FFP publication for service-originated events;
- POS event feed;
- scheduled email.

## Security

### Management endpoints

`/api/management/*` and `/api/month-opening/run` use Windows Integrated Authentication and `CafeteriaManagement` authorization policy. Actor identity comes from `HttpContext.User.Identity.Name`.

### POS/FFP endpoints

`/api/pos/*` and `/api/ffp/*` require `X-Cafeteria-Api-Key` when configured. v88 requires the key outside Development. The production key should come from Bitwarden secret `CafeteriaServicesApiKey`.

## Management endpoints

- `GET /api/management/customers/{customerId}/credit-policy`
- `PUT /api/management/customers/{customerId}/credit-policy`
- `POST /api/management/additional-credit`
- `POST /api/management/stipends`
- `POST /api/month-opening/run?year=YYYY&month=M`

See `FFP-MANAGER-INTEGRATION.md` for the FFP Manager workflow and DTOs.

## POS integration endpoints

- `GET /api/pos/events`
- `GET /api/ffp/customers`
- `GET /api/ffp/holidays`
- `GET /api/ffp/catalog`
- `GET /api/ffp/accounts`
- `POST /api/ffp/ledger`

## Additional Credit

`CustomerCreditPolicies` stores a per-employee Additional Credit Limit. Cumulative approvals for the month may not exceed this value. Additional Credit raises CreditLimit and AvailableBalance compatibility projections but not subsidy.

## Stipends

Cash amount and cafeteria-card reduction are separate. Only CardReductionAmount reduces FFP AvailableBalance.

## Month opening

Monthly allocation operation IDs are deterministic per employee/year/month. Repeated execution is safe.

## Service database

The Services SQLite database is durable production state and must be backed up.
