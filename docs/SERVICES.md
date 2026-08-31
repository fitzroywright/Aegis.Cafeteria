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

## v92 real-time POS wake-up

Cafeteria Services exposes `/hubs/cafeteria` using SignalR. The hub does not carry authoritative financial deltas. After a service event is committed to the durable service-event store, Services broadcasts an `EventsAvailable` notification containing the sequence, customer ID, and event kind.

The POS then executes its existing durable `GET /api/pos/events?afterSequence=...` synchronization. The normal 15-second polling loop remains active as a fallback, so a SignalR outage cannot cause a permanent missed management event.

The SignalR endpoint uses the same `X-Cafeteria-Api-Key` credential as `/api/pos/*` and `/api/ffp/*`.

For an employee already displayed at a POS, successful local application of Additional Credit, stipend, or monthly allocation raises `ICustomerAccountChangeNotifier`; Windows and Linux reload the account and refresh the customer-facing display.

## v93 funding policy and price maintenance

Production Services mode no longer derives funding policy from the `FFP00` LunchItems row. Services owns two audited policy values in its SQLite database:

- Daily Credit Limit Rate
- Daily Company Subsidy Rate

The current policy is used only when a month is opened. Each monthly allocation stores the rates used, so subsequent policy changes are not retroactive.

A first installation may bootstrap the policy from the `FundingPolicy` configuration section. After that, authorized managers maintain it through FFP Manager using the Services management API. The POS does not edit funding policy.

Product Name/Price/Cost remain in `FFP.dbo.LunchItems`. Authorized POS managers update them through Services; POS terminals do not write FFP SQL directly. A successful change broadcasts `CatalogChanged`, causing connected POS terminals to refresh their local catalog. Scheduled refresh remains the fallback.
