# Cafeteria Integration Boundary

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
FFP Manager -> Aegis.Cafeteria.Services (integration plane) -> POS
```

Outbound:

```text
POS -> Aegis.Cafeteria.Services (integration plane) -> FFP Manager EmployeeIDScans
```

The POS never calls FFP Manager or the Integration Plane directly.

## Policy contract

Aegis.Cafeteria.Services is the integration plane and is authoritative for an FFP deployment. Cafeteria does not merge partial policy values from multiple providers.

For a requested period the complete policy contains:

- year and month;
- FFP00-derived daily price rate;
- FFP00-derived daily cost/subsidy rate;
- optional subsidy consumption mode; when omitted, Cafeteria uses `SubsidyConsumptionMode.Standard`;
- the actual working-day dates for the period;
- a policy version;
- an effective-from timestamp.

A policy is rejected as a unit if required fields are incomplete or invalid. Optional policy behaviors must have explicit Cafeteria defaults; adapters do not silently merge values from another provider.

Cafeteria.Services loads the current policy at startup. Management can explicitly reload a period with:

```text
POST /api/management/funding-policy/reload?year=YYYY&month=MM
```

Reloading policy does not silently recalculate an already opened month. The existing explicit month recalculation action remains separately guarded and auditable.

## Employee fact synchronization

Employee synchronization is independent from policy.

The in-process FFP Manager provider inside Aegis.Cafeteria.Services supplies:

- stable employee ID;
- name;
- card identifier;
- active state;
- eligible state;
- effective eligible-from date;
- durable version/change token.

Cafeteria.Services stores the latest employee snapshot and version locally. Change sets are applied to the local snapshot and persisted atomically. A service restart therefore resumes from the durable version instead of comparing records heuristically.

The effective eligible-from date is used with the authoritative working-day calendar to prorate a first-month allocation. Cafeteria does not infer entitlement from a monthly working-day count.

For FFP, the Integration Plane only needs to supply the FFP00-derived price and cost plus the working-day calendar. It may omit the subsidy consumption mode because FFP uses the Cafeteria default, `SubsidyConsumptionMode.Standard`. A future company adapter can explicitly provide another supported enum value when its business rules require one.

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

The canonical transaction is created by Cafeteria. Aegis.Cafeteria.Services translates it through the dynamically loaded FFP Manager provider to the EmployeeIDScans shape.

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

The in-process provider is responsible for translating this contract to EmployeeIDScans and preserving idempotency.

Cafeteria does not construct FFP database rows in its application layer.

## Local-first POS

The POS continues to record sales locally if Cafeteria.Services is unavailable. POS transaction events remain durably queued and retry later.

Loss of FFP Manager must not stop selling; the POS remains local-first and Services retries durable publication.

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

> FFP Manager supplies organizational facts. Cafeteria owns Cafeteria business logic. The Integration Plane translates between them.


## Runtime plugin loading

Cafeteria Services does not reference a concrete integration plugin at compile time.

At startup it reads:

```text
IntegrationPlugin:AssemblyPath
```

and loads that assembly dynamically. The assembly must provide exactly one implementation of each required contract:

- `ICafeteriaIntegrationPlugin`
- `ICafeteriaPolicyProvider`
- `IEmployeeFactSource`
- `IEmployeeIdScanPublisher`
- `IWorkforceReferenceSource`

Services registers those implementations as singletons through the shared Cafeteria application contracts. If the configured assembly is missing, incomplete or ambiguous, startup fails rather than silently selecting another implementation.

The current generic plugin is built separately as:

```text
Aegis.Cafeteria.Plugin.Integration.dll
```

and may be deployed under the configured plugin path, for example:

```text
plugins/Aegis.Cafeteria.Plugin.Integration.dll
```

A different deployment can replace that DLL and configuration without recompiling Cafeteria Services, provided the replacement plugin implements the same shared contracts.

## Naming boundary

Cafeteria core and the generic integration plugin must not contain organization-specific names.

The generic Cafeteria side is:

```text
Cafeteria.Services (integration plane)
    -> dynamically loaded ICafeteriaIntegrationPlugin
    -> FFP Manager provider
```

Organization-specific names appear only in the final adapter that translates the generic integration contracts to that organization's systems. For the current deployment, the dynamically loaded provider runs in-process inside Cafeteria Services and may know about FFP Manager SQL tables such as LunchItems, Employees, Holidays and EmployeeIDScans. Cafeteria domain/application code does not.


## Durable integration state

Cafeteria Services persists integration state in its own SQLite database.

Employee facts and the employee change-feed version are stored in `IntegrationEmployeeCache` and `IntegrationState`. The integration plugin no longer writes a JSON employee cache file.

The last successfully validated policy for each year/month is stored in `ValidatedPolicyCache`. On normal startup the plugin tries the Integration Plane first; if the plane is temporarily unavailable, the last validated policy for that period may be used. An explicit management policy reload remains strict and must reach the authoritative provider.

Plugin replacement is restart-only. Cafeteria Services does not hot-unload or hot-swap integration plugin assemblies. No second HTTP integration-plane service is required.
