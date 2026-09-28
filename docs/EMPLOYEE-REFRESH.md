# Batched employee refresh

Deploy Cafeteria Services and its rebuilt Integration plugin before updating the POS.
The POS requires the authenticated `/api/reference/customers/batch` endpoint; it does
not silently fall back to a full HTTP download if Services is older.

`CafeteriaServices:EmployeeRefreshBatchSize` defaults to 10 and is clamped to 1–100.
Each request reads at most that many SQL employee records (including photos), ordered
by employee ID. The next request starts after the previous batch's last employee ID.
An exact multiple of the batch size ends with an empty terminal request.

Each batch is saved in a local transaction. The Windows Reference Data Status window
and Linux POS show an animated progress bar, saved employee count and completed batch
count. There is no fabricated percentage: the employee total is not queried separately.
The same batching is used by automatic employee refresh. Overlapping employee refreshes
are rejected by the singleton refresh service.

Transient HTTP timeouts/network/server errors retry the same batch twice. Cancellation
and authentication failures are not retried. A failed run keeps committed batches and
untouched cached employees; the next manually started run begins at the first batch
and safely upserts it again. Missing employees are deactivated and their identifiers
removed only after the complete employee enumeration succeeds. Employee IDs rather
than photos are retained across batches. Duplicate identifier claims follow employee
ID order, with the first employee winning, as in the previous full refresh.

This is a batched full refresh, not change tracking or a database snapshot. Changes
made to the source during enumeration may require another refresh. The POS adds a nullable credit-policy version timestamp to its customer cache on startup;
existing financial transactions are not rewritten. The existing per-request timeout remains in effect for each small batch.

## Verification

Run the unit and persistence test projects on .NET 10. New tests cover batch commit
ordering, ID gaps, finalization, failed/cancelled reads, invalid cursors, overlapping
refresh attempts, safe repeat upserts, and duplicate identifier claims across batches.
Then refresh against FFPMANAGER_TEST with more than ten employees, observe progress,
and interrupt Services after a saved batch. Confirm unvisited employees remain usable
and a subsequent refresh completes. The graphical clients and live SQL integration
require validation in the target environment.


## Non-modal refresh and credit changes

The Windows details form is non-modal and observes a service-owned background task.
Closing or reopening it does not stop the refresh or start a duplicate. Both POS clients
retain progress and completion/failure status on the main window. Refresh completion
does not reselect the current customer, rebuild the cart, or reload product buttons.
Existing management-event notifications preserve the original scan identification.

The calculator already floors spendable credit at zero. A separate signed capacity
and shortfall expose an over-limit condition without generating another debt charge.
The cashier sees the shortfall and cash-only warning; existing amount-due and payroll
calculations continue to own liability. Additional credit restores spending capacity;
it is not a payment and does not itself settle debt. Refunds and payments retain their
existing ledger treatment, and cash for a new meal does not settle an earlier debt.

Every new locally committed card/credit sale re-reads eligibility, allocation, purchases,
refunds and management events inside its database transaction. It rejects card tender
above current capacity and rejects both card and credit tender while the account is
over its capacity. Cash-only purchases remain available. A rejection commits neither
the order nor its outgoing transaction, so the cashier can review tender and retry.
Stipend/credit events that expose a shortfall produce a warning with employee and operation IDs.

The Services policy update timestamp now travels in employee refresh responses and
Additional Credit events. POS persists that version and ignores older or equal policy
updates. An unversioned legacy event cannot replace a versioned limit. This prevents
a delayed refresh from undoing a newer credit-limit update, including across restarts.

This change does not provide central reservation of funds across tills. An event not
yet delivered to a till cannot be checked locally; offline or delayed tills retain that
existing risk. The local transactional guard must not be described as a cross-terminal
spending guarantee. Month-end debt carry-forward and payroll settlement rules are unchanged.
