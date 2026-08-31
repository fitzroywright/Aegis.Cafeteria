# Aegis.Cafeteria v26

- Fixes empty customer list diagnostics/startup behavior.
- FFP employee import now runs explicitly after local SQLite initialization and before the main form is shown.
- Employee, holiday, and rate refresh operations are separated so missing rate configuration cannot obscure employee loading.
- Startup shows the actual FFP SQL error if JMDB01/FFPJM employee loading fails and warns if the query returns zero rows.
- Background refresh remains enabled for ongoing updates.
- Preserves all v25 and earlier fixes.
