# Aegis.Cafeteria v27

## Employee cache observability
- Added `IFfpEmployeeCacheStatus` and a durable-cache-aware implementation backed by the existing local SQLite customer identifier projection.
- Tracks active cached employee count, identifier count, cache timestamp/age, last refresh attempt, last successful refresh, in-progress state and last error.
- No SQLite schema migration is required.
- Windows POS status bar now shows employee cache count and age with green/amber/red aging thresholds.
- Clicking the Windows cache indicator opens full cache diagnostics and a **Refresh now** action.
- Linux POS shows the same employee cache count/age state and provides a manual employee refresh action.
- Employee cache warning/critical ages are configurable (`120` and `480` minutes by default).

## Monitoring
- Zabbix heartbeat JSON now includes employee-cache count, identifier count, age, last attempt, last success, last error, refresh-in-progress and health fields.
- Overall heartbeat becomes degraded when the employee cache is empty or older than the configured critical threshold.
- Graylog heartbeat messages include employee-cache count and age.

## FFP refresh reliability
- Employee, holiday and credit-rate background refreshes are isolated from one another.
- A missing/unconfigured credit-rate query no longer causes employee or holiday refresh to be reported as failed.
- Windows and Linux POS both perform an employee refresh during startup before normal operation continues.

## Integration testing
- Added an application-to-SQLite integration test that runs `FfpReferenceRefreshService` through `EfFfpReferenceCache` and verifies employees, identifiers, eligibility and cache health.
- Added a failure-path integration test proving a failed FFP refresh preserves the last durable employee cache and records the error.
- Added an opt-in live SQL Server integration test. Set `AEGIS_FFP_LIVE_TEST=1`; optionally set `AEGIS_FFP_TEST_CONNECTION_STRING`, otherwise the legacy Windows Integrated Security JMDB01/FFPJM connection is used.
- TestHost now exposes `/health/ffp` when FFP integration is enabled to report live employee and current-month holiday counts.

## Preserved behavior
All v26 and earlier fixes are retained, including the polished customer UI, reporting, FFP Employees/Holidays integration, weekend/holiday working-day exclusion, Graylog, Zabbix heartbeat, weekly messaging, durable FFP ledger publication, Development secrets and SQLite DateTimeOffset fixes.
