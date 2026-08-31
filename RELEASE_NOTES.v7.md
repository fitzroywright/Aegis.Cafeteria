# Aegis.Cafeteria v7

## Aegis Engineering Diagnostics

- Adds the `Aegis.Engineering.Diagnostics` diagnostic protocol to Cafeteria Services.
- Adds diagnostic Levels 5 through 1: Scan, Analysis, Verification, Repair Gate, and Critical Intervention Gate.
- Persists diagnostic runs, checks, evidence, status, requesting engineer, reason, resolution, and resolution identity/time.
- Adds a protected Services dashboard at `/engineering/diagnostics` with an LCARS-inspired presentation.
- Adds protected APIs to initiate diagnostics, review recent runs, inspect a run, and resolve a run.
- Development diagnostics require an authenticated Windows identity but do not require the configured production management group. Production diagnostics retain the configured management-group boundary.
- Level 2 and Level 1 require an engineering reason and do not execute destructive repair automatically.
- Adds `Engineering > Aegis Engineering Diagnostics...` to both Windows and Linux POS clients; both open the centralized Services dashboard.
- The repository and application use `Aegis.Engineering.Diagnostics` naming throughout. Fictional protocol terminology is not used in application code, namespaces, APIs, or documentation.

## Existing behavior retained

v7 retains all v6 financial correctness, stipend validation, Development reset, local product administration, FFP integration, reporting, and Windows/Linux POS behavior.
