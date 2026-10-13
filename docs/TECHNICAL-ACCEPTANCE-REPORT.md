# Cafeteria technical acceptance report

**Date:** October 13, 2026  
**Branch:** `feature/cafeteria-permanent-acceptance` in Aegis.Cafeteria, Common.Diagnostics and Aegis.Operations  
**Cafeteria base:** PR #30 merge `78ce4b38eaa4cb414574d149d6edaf83d20fc15a`  
**Disposition:** Implementation checkpoint; not technically accepted and not ready to merge.

## Qualification decision

| Milestone | Decision | Evidence |
| --- | --- | --- |
| Development Complete | Not established | Framework code and local checks pass; live proofs and release concerns remain |
| Integration Proven | **No** | Zero of twenty mandatory business proofs executed successfully |
| Ready for User Acceptance | **No** | Integration, deployed production inventory, deployment checks and release sign-off are incomplete |

The user's completion criteria have **not** been met. No legacy function was disabled,
no branch was merged to main, and no production financial data or schema was modified.

## Implemented checkpoint

- An isolated command in the real Services executable; explicit Level 1 / Level 2 selection.
- Real Services and POS SQLite stores per execution after database safety verification.
- Real integration plugin loading with a reviewed binary fingerprint; real service and
  application methods, real SQLite persistence, and SQL-backed integration assertions.
- Twenty proof definitions, isolated fixture filtering, expected/actual results, per-proof
  checkpoints, and recovery from an external commit without a local acknowledgement.
- Deduction-only stipend tests using Amount, Reason and ProcessedBy; outbound scan type 4;
  duplicate prevention, insufficient-credit rejection, and a post-POS-spending balance check.
- An eligibility restoration journal before dedicated FFP employee mutations.
- Common.Diagnostics evidence contracts and its existing engineering diagnostic run store;
  a read-only Services acceptance-evidence diagnostic preserving the five-level contracts.
- Operations history, individual proofs, versions, schema fingerprints, diagnostic details,
  an authorized complete-suite action, and explicit engineering/deployment qualification.
- On-demand, post-deployment and release qualification execution scripts and a commissioning
  runbook. Existing deployment scripts do not deploy Cafeteria Services; automatic integration
  into an actual Cafeteria deployment pipeline remains to be commissioned.

## Local validation

| Check | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: |
| Cafeteria unit tests | 253 | 0 | 0 |
| Cafeteria persistence tests | 71 | 0 | 0 |
| Cafeteria reporting tests | 20 | 0 | 0 |
| **Cafeteria total** | **344** | **0** | **0** |
| Common.Diagnostics tests | 114 | 0 | 0 |
| Aegis.Operations tests | 38 | 0 | 0 |
| **All locally executed test projects** | **496** | **0** | **0** |

Cafeteria Services and Operations compile in Release with their Common dependencies.
The full three-project Cafeteria test suite was rerun after PR #30 synchronization and
framework integration. PR #30's current stipend contracts were preserved. New framework
checks do not restore StipendPaymentMode, CashAmount or CardReductionAmount to stipend
contracts. Existing refund tender fields retain their separate business meaning.

The local runner-host test constructs the real FFP plugin and actual worker registrations,
initializes a real isolated Services SQLite schema, and verifies no automatic hosted worker
is registered. Static connection/path tests reject FFPJM, other catalogs, ambient credentials,
wrong servers/logins, failover/attach targets, missing production inventory, overlapping
paths, symlink ancestors and unreviewed integration binaries. Missing credentials produce
blocked evidence without creating either financial SQLite store.

These checks establish local regression and guard behavior, **not** live FFP integration.
`FFPMANAGER_TEST` and the dedicated acceptance connection are absent here. Three existing
optional FFP contract tests return without accessing SQL when their variable is missing;
their ordinary test-run pass counts contribute no FFP acceptance evidence.

Windows FFP Manager UI interaction, live AD authentication, graphical POS execution,
published server operation and production deployment verification were not executed here.
The supplied Windows PR #30 result (323 passing tests) is a baseline, not a substitute for
this framework's mandatory bridge proofs.

## Actual acceptance execution

The real acceptance command executed and exited **2 (blocked)**. The corresponding JSON
is saved in `docs/acceptance-evidence/blocked-environment.json`. It reports isolation as
unverified and full integration as false. No FFP SQL connection was attempted after the
missing dedicated credential check, and no acceptance financial SQLite database was created.

| Outcome | Mandatory proof count |
| --- | ---: |
| Passed | 0 |
| Failed | 0 |
| Blocked | 20 |
| Unexecuted without a recorded blocker | 0 |

All twenty business scenarios are **unexecuted in substance**: each has an explicit Blocked
outcome because the prerequisite environment is unavailable. Zero failed proofs does not
mean those business workflows are correct. Service/FFP schema fingerprints could not be
observed in this blocked execution; the local schema version is a compiled contract value.

| Proof | Verification | Actual outcome | Blocker |
| --- | --- | --- | --- |
| CAF-01 | FFP00 policy retrieval and interpretation | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-02 | Employee fields and eligibility | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-03 | Holidays and working days | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-04 | Automatic credit limit | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-05 | FFP00 subsidy limit | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-06 | FFP credit and balance synchronization | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-07 | ID scan publication | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-08 | Services stipend deduction | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-09 | Services additional credit | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-10 | Monthly opening and initialization | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-11 | Closing and rollover | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-12 | Unused positive credit expiration | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-13 | Outstanding debt carry-forward | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-14 | Refunds, voids and prior-period credits | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-15 | Payroll calculation and integration | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-16 | Mid-month employee and eligibility changes | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-17 | Cafeteria / FFP reconciliation | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-18 | Idempotency and duplicate prevention | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-19 | Interrupted publication and recovery | Blocked | Dedicated FFP acceptance credentials unavailable |
| CAF-20 | Complete financial workflow and manager bridge | Blocked | Dedicated FFP acceptance credentials unavailable |

## Environment blockers

1. Dedicated SQL acceptance credentials for the existing FFP test database are unavailable.
2. Physical production database paths have not been verified on the deployed host. Source
   defaults identify `cafeteria.services.db` and `cafeteria.services.runtime.db`; the Linux
   unit sets the service store to `/var/lib/aegis-cafeteria-services/cafeteria.services.db`.
   Windows examples use `C:\ProgramData\Aegis.Cafeteria.Services\cafeteria.services.db`.
   Environment, remote configuration and working-directory overrides remain authoritative;
   these repository values are not asserted to be the deployed inventory.
3. Dedicated CAF-ACCEPT- employee IDs and independently verified FFP00/holiday fixtures
   have not been provisioned in this environment.
4. Live SQL least-privilege, metadata visibility and dependency guards have not executed.
   Enabled triggers and other executable indirect paths currently block acceptance. There
   is no unsafe bypass or automatic schema/trigger change.

## Remaining implementation and release concerns

A downstream payroll delivery/import acknowledgement is absent from the inspected
contracts. CAF-15 deliberately does not turn generated CSV into a successful downstream
integration claim. That actual integration contract must be implemented and proven; it
is a remaining implementation gap, not merely a missing password.

Source review also identified mandatory financial questions that the live scenarios must
resolve. These are not counted as executed failed proofs in the table above:

- The Services ledger query excludes PosTransaction events, while stipend snapshots and
  payroll read that ledger. The suite checks the Services balance after a real persisted
  POS purchase and asserts the remaining employee payroll liability.
- The outbound POS worker reads the order Total for partial refund publication. The suite
  requires the real FFP refund scan amount to equal the individual refund delta.
- The Services snapshot currently supplies no carried-debt value. The suite requires
  nonzero source-period debt in the following period rather than accepting a zero fixture.
- The bridge publisher inserts EmployeeIDScans but does not directly update employee
  CreditLimit/AvailableBalance columns. The actual supported projection path must be
  identified and verified without allowing indirect production dependencies. CAF-06 asserts
  those summary values and will not infer synchronization from a successfully inserted scan.

These issues require real acceptance execution and, where confirmed, application fixes.
The runner must not be weakened or its expected values changed to hide failures.

## Legacy replacement evidence

| Legacy action | Required replacement evidence | Current decision |
| --- | --- | --- |
| Monthly Credit Allocation | CAF-04, 05, 06, 10, 17, 18 | Retirement not established |
| Stipend Allocation | CAF-08, 17, 18, 19 | Retirement not established |
| Additional Credit | CAF-09, 17, 18, 19 | Retirement not established |

Only an authorized human retirement decision may disable legacy functions. A successful
suite alone does not perform that action.

## Completion path

Commission the dedicated credentials, verified production inventory and owned fixtures
using `PERMANENT-ACCEPTANCE.md`. Resolve the payroll acknowledgement boundary and relevant
financial defects. Run the complete Level 1 suite against the designated real test environment,
retain all evidence, perform the required code review, and then verify the deployment.
Do not merge or claim integration proven until every mandatory proof passes.
