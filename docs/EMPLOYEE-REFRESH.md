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
made to the source during enumeration may require another refresh. No schema migration
is required. The existing per-request timeout remains in effect for each small batch.

## Verification

Run the unit and persistence test projects on .NET 10. New tests cover batch commit
ordering, ID gaps, finalization, failed/cancelled reads, invalid cursors, overlapping
refresh attempts, safe repeat upserts, and duplicate identifier claims across batches.
Then refresh against FFPMANAGER_TEST with more than ten employees, observe progress,
and interrupt Services after a saved batch. Confirm unvisited employees remain usable
and a subsequent refresh completes. The graphical clients and live SQL integration
require validation in the target environment.
