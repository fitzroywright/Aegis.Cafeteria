# Operations and Deployment Guide

## Runtime components

Production deployments may include:

- Aegis.Cafeteria.Services
- Aegis.Cafeteria.Management
- Windows POS terminals
- Linux POS terminals
- Graylog GELF UDP input
- SMTP relay
- FFP SQL Server

## Cafeteria Services

Run Services under a service identity that has the required Windows/AD access to FFP SQL. The FFP connection must use Integrated Security / Trusted Connection. No SQL username/password is required or desired.

Expected SQL transport model: TCP, trusted connection, Encrypt=False. Keep environment-specific host/database settings in configuration.

Services owns scheduled workers including month opening and scheduled email. Restart Services after changing configuration that is loaded at startup.

## Management Console

Management is a separate web application backed by Cafeteria Services. It stores only management session state and proxies authorized operations to Services. Common.Security authentication happens through Services.

## Graylog

Configure a GELF UDP input on:

- Host: 10.10.0.3
- Port: 1699/UDP

Syslog UDP is not used by this release baseline.

## SMTP

The configured internal relay can be used without embedding an SMTP password when the relay/network policy permits it. Use the Management Console Test Email function after deployment.

## Data and backup

Back up the Services persistence database and any application configuration required to rebuild the deployment. Keep runtime SQLite WAL/SHM files out of source control. Verify restore procedures before production cutover.

## Startup validation

After deployment:

1. Start Cafeteria Services.
2. Confirm Services health endpoint responds.
3. Confirm FFP customer retrieval succeeds.
4. Confirm Graylog receives GELF messages.
5. Start Management and authenticate.
6. Search/select an employee in Management.
7. Load audit history.
8. Send a test email.
9. Start a POS terminal and confirm the splash transitions to the focused main window.
10. Verify employee search, cached photo display, a test transaction, tender behavior, customer-facing display, and refund chooser.

## Production safety

Do not enable DevelopmentData in production. Do not use the development reset endpoint in production. Do not reintroduce direct FFP SQL access into the POS.
