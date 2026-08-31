# Aegis.Cafeteria v14

Persistence test reliability release.

- Added shared SqliteTestDatabase helper.
- All PersistenceTests temporary SQLite DBs use helper-created unique paths and helper cleanup.
- Cleanup clears Microsoft.Data.Sqlite pools and deletes SQLite main/WAL/SHM/journal files with retry handling for Windows file locks.
- Added explicit Microsoft.Data.Sqlite 10.0.11 package reference to PersistenceTests.
- Period-sensitive CustomerAccountQueryTests timestamps are fixed to August 2026.
- No production behavior changes.
