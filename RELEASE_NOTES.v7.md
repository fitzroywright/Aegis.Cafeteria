# Aegis.Cafeteria v7

## Starfleet Engineering Protocols

- Adds Starfleet Engineering diagnostic Levels 5 through 1.
- Adds an LCARS-style Services dashboard at `/engineering/diagnostics`.
- Adds authenticated diagnostic APIs for running, viewing, and resolving diagnostic runs.
- Persists diagnostic evidence and resolution history independently of POS transaction data.
- Level 5 performs baseline Services database, funding-policy, and eligible-employee checks.
- Level 4 adds FFP holiday-source and publication-queue analysis.
- Level 3 adds service-event verification.
- Level 2 and Level 1 require an engineering reason and stop at an explicit intervention gate; destructive repair is never executed automatically by a diagnostic run.
- Adds Engineering -> Starfleet Engineering Diagnostics entry points to Windows and Linux POS clients.
- Uses the existing Cafeteria Services base URL, allowing the LCARS console to be hosted remotely when Services is remotely reachable.

## Safety

- Existing cafeteria financial transaction semantics are unchanged.
- Diagnostics are read-only except for their own diagnostic history and resolution records.
- Management authentication remains required.
