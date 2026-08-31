# Aegis.Cafeteria v21

* Rebased on v20 and retained every v13-v20 startup, SQLite, outbox, logging and development-secret fix.
* FFP `Employees` is now the employee/customer authority. Eligibility, NFC, barcode/code, TRN, name and photo are cached locally.
* FFP holidays and stipend/subsidy daily rates are the external calculation inputs.
* Added a durable local FFP ledger outbox; checkout stays offline-capable. The publisher uses idempotent `EmployeeIDScans.TransmissionID`, with atomic Employee `CreditLimit` / `AvailableBalance` updates.
* Monthly allocations are persisted in Aegis and published to the FFP ledger/current Employee summary.
* Completed cafeteria-account purchases publish their account tender (not cash) to FFP.
* `TotalCreditLimit` explicitly includes base monthly credit + approved additional credit + applicable prior-period refund credit.
* Startup creates SQLite/log/heartbeat parent directories, eliminating the ProgramData "unable to open database file" failure.
* Graylog GELF remains the primary logging destination; Serilog remains absent.
* Added atomic JSON heartbeat for Zabbix plus periodic Graylog heartbeat events.
* Added weekly customer transaction summaries through a shared Aegis employee-messaging abstraction with durable duplicate prevention.
* Rebuilt documentation around the FFP authority/ledger contract and added tests for the new behavior.
