# Aegis.Cafeteria - First Release Candidate

This document identifies the first release candidate baseline for Aegis.Cafeteria.

## Release intent

This baseline is the first version intended to be treated as a frozen release candidate. It contains the Windows POS, Linux POS, Cafeteria Services, Management Console, reporting, FFP integration, audit trail, scheduled email, diagnostics, GELF logging, and current financial rules.

## Architecture baseline

- POS clients do not connect directly to the FFP SQL database.
- POS clients communicate with Aegis.Cafeteria.Services.
- Aegis.Cafeteria.Services owns trusted FFP SQL access using Windows Integrated Security.
- FFP SQL access is intended to use TCP, Trusted Connection / Integrated Security, and Encrypt=False unless later changed by an explicit release decision.
- No Bitwarden runtime dependency is required for the normal Cafeteria path.
- Graylog logging uses GELF UDP to 10.10.0.3:1699.
- Audit records are persisted in the Services audit table and also emitted to Graylog.
- Management authentication is provided through Common.Security and the configured FFP Active Directory environment.

## Functional baseline

The Management Console includes employee search, employee credit policy, additional credit, stipends, funding rates, month opening/recalculation, employee amount-due reporting, payroll report send/resend, manager weekly report settings, test email, audit trail, diagnostics, and service status.

The POS includes employee lookup, customer-facing display, purchase/tender flow, refunds, customer cash payments, payroll deductions, outstanding-balance handling, cached photos, reporting, startup Services communication, and the current customer transaction presentation rules.

## Release immutability

The exact commit SHA recorded for this release candidate is the authoritative identity of the release. Future development must occur on a separate development branch. Master is expected to point to the exact same SHA as this release candidate baseline.

Do not rewrite, amend, squash, rebase, or force-update the release-candidate reference once established.
