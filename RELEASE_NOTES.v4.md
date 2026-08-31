# Aegis.Cafeteria v4

## Development Reset SQL Transaction Fix

v4 fixes the Development reset failure reported when `POST /api/development/reset` reached the FFP SQL reset transaction.

The reset query returns deleted-ledger and reset-account counts through a `SqlDataReader`. In v3 the transaction was committed while that reader was still open, causing SQL Server to throw:

`There is already an open DataReader associated with this Connection which must be closed first.`

v4 scopes and disposes the reset command/reader before `CommitAsync()` is called. Reset semantics are otherwise unchanged.
