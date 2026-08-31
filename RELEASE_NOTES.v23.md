# Aegis.Cafeteria v23

## FFP employee and holiday integration finalized

- Uses the Windows Integrated Security SQL Server connection string found in the legacy `FFP.LunchSystem` `App.config` by default: `Data Source=JMDB01;Initial Catalog=FFPJM`.
- Employee loading is ported from the legacy `GetEmployeeListSQL` + `SystemFunctions.GetData` behavior.
- Reads `ID`, `Code`, `TRN`, `NFCID`, `RFID`, name, ticket counts, photo, temporary barcode/NFC, `CreditLimit`, and `AvailableBalance` from `Employees`.
- Preserves legacy identifier fallback behavior: blank `Code`, `TRN`, `NFCID`, and `RFID` fall back to the Employee ID.
- Loads all employees so `EligibleForLunchTicket` is explicitly checked by Aegis instead of making an ineligible employee appear nonexistent.
- Reads holidays from `FFPJM.dbo.Holidays` using `Name, CalendarDate` for the requested year.
- Monthly eligible working days exclude Saturdays, Sundays, and FFP holiday dates.
- Keeps v22 reporting, Graylog, Zabbix heartbeat, FFP durable outbox, weekly messaging, development secrets, and all SQLite DateTimeOffset regression fixes.

The stipend/subsidy daily-rate query remains configurable because its production FFP table/schema has not yet been supplied.
