# Database artifacts — v48

v48 intentionally ships no central Cafeteria SQL database creation script. Production storage is:

1. the existing FFP SQL database (`Employees`, `LunchItems`, `Holidays`, `EmployeeIDScans`); and
2. the local SQLite database created from the v48 EF model.

See `docs/DATABASE-DEPLOYMENT.md` for the greenfield SQLite procedure.
