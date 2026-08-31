# Aegis.Cafeteria v72

- FFP EmployeeIDScans now preserves the originating operator username for human transactions instead of hard-coding `AEGIS.CAFETERIA`.
- Automatic monthly allocation publishes `UserName = SYSTEM`.
- Monthly allocation description follows the legacy FFP Assign Tickets style: `{working days} new Tickets Assigned to {employee name} By SYSTEM`.
- Monthly allocation publishes `IDName = Tickets Assigned`.
- Purchase descriptions include ordered product names/quantities and order total; split tender also states the cafeteria-account amount.
- Refund descriptions include refunded product names/quantities and cafeteria-account refund amount.
- Durable FFP outbox stores `UserName` and `IdName` so retries preserve the original actor.
- Local SQLite schema version is 72. Existing earlier development databases must be deleted before first v72 run.
