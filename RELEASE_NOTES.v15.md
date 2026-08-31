# Aegis.Cafeteria v15

## SQLite DateTimeOffset query fix

- Fixed `EfCustomerAccountQuery` so SQLite no longer executes `ORDER BY` against `DateTimeOffset` (`AccountEventRow.OccurredAt`). Matching account events are filtered in SQL, materialized, then ordered in memory by `OccurredAt` and `Id`.
- Fixed the same latent issue in `EfSynchronizationService` for `OutboxRow.CreatedAt`. Pending/failed rows are materialized, then ordered in memory before applying the configured push batch size.
- No financial rules, Secrets Manager behavior, UI behavior, or logging architecture changed.
- This release specifically addresses the SQLite provider exception: `SQLite does not support expressions of type DateTimeOffset in ORDER BY clauses`.
