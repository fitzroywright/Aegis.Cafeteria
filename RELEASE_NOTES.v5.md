# Aegis.Cafeteria v5

## Fixed

- `CafeteriaHeartbeatWorker` no longer leaks `TaskCanceledException` during normal host shutdown.
- The heartbeat interval delay is now inside the cancellation-aware `try/catch` and exits cleanly when `stoppingToken` is cancelled.

## Behavior

- No functional heartbeat behavior changed while the application is running.
- Normal application close is treated as expected cooperative cancellation rather than an error.
