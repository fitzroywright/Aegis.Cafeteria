# Aegis.Cafeteria v8 Release Notes

## Purpose

v8 is a design-review release. It reconciles the accumulated business rules, removes architectural contradictions, improves POS latency and offline behavior, and separates the cafeteria POS from the FFP Application administrative role.

## Major changes

- Replaced fixed/legacy per-working-day customer credit fields with immutable `MonthlyCreditAllocation` snapshots.
- Added month-opening batch generation using daily stipend/subsidy rates, eligibility, working days and the existing-holiday-table adapter.
- Added explicit `CarriedForwardDebt` and payroll collection reconciliation.
- Unused positive balances expire; unpaid debt carries forward.
- Customer payments now reopen available revolving credit while remaining capped by approved capacity.
- Added indexed customer identifiers: NFC, barcode and TRN; manual search remains local and lightweight.
- Reworked account read model for current-month transactions, monthly allocation components, carried debt, subsidy/non-subsidy availability, payroll liability and sync state.
- Removed timer-based application-data synchronization. `TriggeredSynchronizationWorker` waits for event signals only.
- Added SignalR stateful reconnect and indefinite connection retry; notifications wake sync but do not carry financial truth.
- Central writes persist `ChangeJournal` before SignalR notification. Notification failures after commit are logged and do not turn successful financial writes into apparent failures.
- Split DI composition into explicit POS and FFP/admin roots.
- Defined data ownership: central/admin owns customers, identifiers, products, holiday/rate reference data and approvals; POS owns operational transaction events; allocation snapshots are immutable.
- Central-owned reference cache writes on the POS no longer create outgoing outbox entries.
- Local SQLite now initializes WAL, FULL synchronous durability, foreign keys and busy timeout.
- Mapped Aegis holiday synchronization data to `CafeteriaHolidayCache`, avoiding collision with FFP's existing holiday table.
- Central `EnsureCreated` remains blocked by default; production requires migrations.
- Added/updated tests for allocations, debt carry-forward, payment-restored availability, identification, no-polling synchronization, central admin notification behavior, and ownership semantics.

## GoF patterns deliberately used

- Strategy: calendar/rate/allocation providers and interchangeable persistence/provider policies.
- Adapter: FFP existing holiday table and central infrastructure integrations.
- Observer: SignalR change notifications and local account-change events.
- Decorator: persistence logging.
- Factory: account-period creation and EF context factories.

A full State or Command/Mediator architecture was intentionally not introduced because the existing guarded order state machine and application services are clearer and cheaper for the current scope.

## Release gate

The generation container has no .NET SDK. Before deployment, run restore/build/test on .NET 10, generate/review provider-specific EF migrations, and exercise SQL Server/PostgreSQL/SQLite integration tests in the intended deployment environment.
