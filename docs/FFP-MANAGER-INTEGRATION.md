# FFP Manager Integration Boundary

## Objective

Cafeteria is an independent application. FFP Manager supplies organizational facts and receives employee ID scan projections. Cafeteria owns Cafeteria business rules, balances, allocations, transactions and reports.

The supported boundary is:

- policy facts in;
- employee facts in;
- employee ID scans out.

Reports never cross this boundary.

## Runtime path

Inbound:

```text
FFP Manager -> FFP Integration Plane -> Aegis.Cafeteria.Services -> POS
```

Outbound:

```text
POS -> Aegis.Cafeteria.Services -> FFP Integration Plane -> FFP Manager EmployeeIDScans
```

The POS never calls FFP Manager or the Integration Plane directly.

## Policy contract

The Integration Plane is authoritative for an FFP deployment. Cafeteria does not merge partial policy values from multiple providers.

For a requested period the complete policy contains:

- year and month;
- FFP00-derived daily price rate;
- FFP00-derived daily cost/subsidy rate;
- subsidy consumption mode;
- the actual working-day dates for the period;
- a policy version;
- an effective-from timestamp.

A policy is rejected as a unit if it is incomplete or invalid.

Cafeteria.Services loads the current policy at startup. Management can explicitly reload a period with:

```text
POST /api/management/funding-policy/reload?year=YYYY&month=MM
```

Reloading policy does not silently recalculate an already opened month. The existing explicit month recalculation action remains separately guarded and auditable.

## Employee fact synchronization

Employee synchronization is independent from policy.

The Integration Plane change feed supplies:

- stable employee ID;
- name;
- card identifier;
- active state;
- eligible state;
- effective eligible-from date;
- durable version/change token.

Cafeteria.Services stores the latest employee snapshot and version locally. Change sets are applied to the local snapshot and persisted atomically. A service restart therefore resumes from the durable version instead of comparing records heuristically.

The effective eligible-from date is used with the authoritative working-day calendar to prorate a first-month allocation. Cafeteria does not infer entitlement from a monthly working-day count.

## Allocation ownership

Cafeteria.Services owns allocation logic.

For a newly eligible employee:

```text
eligible working days = policy working-day dates on or after EligibleFrom
credit limit = eligible working days x daily price rate
subsidy allocation = eligible working days x daily cost rate
```

FFP Manager does not calculate Cafeteria entitlement, balances or subsidy consumption.

## Outbound transactions

The canonical transaction is created by Cafeteria. The Integration Plane translates it to the FFP Manager EmployeeIDScans shape.

The outbound contract includes:

- transaction ID / idempotency key;
- employee ID;
- transaction kind;
- occurred-at timestamp;
- amount;
- description;
- identification method;
- user name;
- optional identification code.

The Integration Plane is responsible for translating this contract to EmployeeIDScans and preserving idempotency.

Cafeteria does not construct FFP database rows in its application layer.

## Local-first POS

The POS continues to record sales locally if Cafeteria.Services is unavailable. POS transaction events remain durably queued and retry later.

Loss of FFP Manager or the Integration Plane must not stop selling.

## Reports

Cafeteria reports are generated from Cafeteria data and delivered directly to people through the supported UI/email/export channels.

There is no report synchronization API to FFP Manager.

## FFP Manager changes

FFP Manager should only expose the facts required by the Integration Plane.

If missing, add a reliable employee modification marker such as `ModifiedAtUtc` or a row version. The Integration Plane should use that durable marker to build its employee change feed.

Do not add the following to FFP Manager:

- Cafeteria allocation logic;
- Cafeteria balance logic;
- subsidy consumption calculations;
- Cafeteria reporting;
- Cafeteria synchronization state.

## Architectural invariant

> FFP Manager supplies organizational facts. Cafeteria owns Cafeteria business logic. The FFP Integration Plane translates between them.
