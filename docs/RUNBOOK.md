# Aegis.Cafeteria Operational Runbook

## Purpose
Provides local-first cafeteria POS and central cafeteria services.

## Dependencies
POS depends on its local SQLite store for local operation. Central synchronization uses Cafeteria Services. Services depend on their configured database and optional messaging/secrets integrations.

## Startup
Verify local persistence, terminal identity and registration. POS may start offline when local persistence is healthy.

## Shutdown
Allow the process to stop normally. Preserve the local SQLite database, outbox and registration identity.

## Configuration
Use Aegis.Configuration readiness for required/optional settings. Do not expose secret values.

## Registration
Preserve the durable registration identity across deploys and restarts.

## Secrets
Common.Secrets health is authoritative. Configuration presence alone is not proof of provider health.

## Health Verification
Check application diagnostics, local store health, service/database reachability and POS synchronization telemetry.

## Normal State
Local store healthy; application diagnostics fresh; synchronization queue empty or draining; no failed/dead-letter work.

## Common Warnings
Pending outbox items while connectivity is impaired are expected offline behavior. A growing oldest-pending age while online requires investigation.

## Common Failures
Local store unavailable/corrupt, repeated synchronization failures, central database failure, or stale/missing diagnostic evidence.

## Recovery
Restore connectivity or the failed dependency while preserving the local outbox. Confirm queued transactions drain after recovery.

## Backup / Restore
Back up central data and local durable POS state according to site policy. Restore must preserve transaction identity and prevent duplicate replay.

## Deployment / Rollback
Deploy binaries without deleting POS SQLite/outbox or registration identity. Rollback application binaries while retaining durable state.

## Purge / Re-registration
Purge is destructive and must not be used as ordinary troubleshooting. Re-register only through the Common.Registration lifecycle.

## Escalation
Escalate persistent transaction replay failure, local-store integrity failure or financial-data inconsistency.

## Log Locations
Use service/host logs and Operations correlation IDs. Never log card credentials, registration credentials, API keys or secret values.
