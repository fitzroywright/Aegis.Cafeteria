# Aegis.Cafeteria.Services on Linux

Aegis.Cafeteria.Services can run as a normal .NET 11 ASP.NET Core process under systemd. The application keeps the same zero-application-secret model used on Windows.

## Authentication model

- POS and Management authenticate users through Cafeteria Services/Common.Security and use short-lived session tokens.
- FFP SQL uses `Integrated Security=True`; no SQL username or password is configured in the application.
- On Linux, SQL Integrated Security is provided by Kerberos/GSSAPI. The Linux host must be joined to `fftpj.local` and able to obtain a Kerberos ticket for the identity SQL Server authorizes.
- SMTP uses the trusted relay `smtp.fftpj.local:25`; the application supplies no SMTP username or password. The relay should authorize the Linux Services host/IP.

The OS/domain join maintains Kerberos machine credentials. Those are operating-system credentials, not Aegis application secrets, and must be protected by normal Linux file permissions and AD controls.

## Recommended host setup

Use Debian 13 or another supported Linux distribution with .NET 11, Kerberos, realmd/SSSD and DNS correctly pointing at the FFTPJ AD DNS servers.

Create a locked-down local runtime account:

```bash
sudo useradd --system --home /var/lib/aegis-cafeteria-services --shell /usr/sbin/nologin aegis-cafeteria
```

Join the host to `fftpj.local` using the organization's normal realmd/SSSD procedure and verify Kerberos before installing the application. The exact AD principal used for SQL should be granted only the FFP database permissions required by Cafeteria Services; do not grant `db_owner`.

Verify the SQL Server SPN/Kerberos path and then verify a ticket can be obtained with `klist`/`kinit` according to the site's AD policy. If using the domain-joined computer account for SQL access, grant SQL access to that computer account. If using a dedicated AD service identity, manage its Kerberos material at the OS layer rather than putting a password in Aegis configuration.

## Install

Publish the Services project for Linux and copy it to:

```text
/opt/aegis-cafeteria-services
```

Copy `aegis-cafeteria-services.service` to:

```text
/etc/systemd/system/aegis-cafeteria-services.service
```

Then:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now aegis-cafeteria-services
sudo systemctl status aegis-cafeteria-services
```

The unit overrides the Windows SQLite path with `/var/lib/aegis-cafeteria-services/cafeteria.services.db` and supplies only non-secret FFP SQL coordinates (`JMDB01` / `FFP`).

## SMTP

No code or credential change is required for Linux. Services sends through:

```text
smtp.fftpj.local:25
From: cafeteria@ffpja.org
```

Authorize the Linux server's IP on the internal SMTP relay, then send a controlled test message. The scheduled-email queue will retain failed messages and retry later.

## Production verification

Before production use, verify all of the following:

1. `systemctl status aegis-cafeteria-services` is healthy.
2. `/health` responds.
3. Kerberos-authenticated connection to `JMDB01` / `FFP` succeeds without a SQL password.
4. Employee/holiday/lunch-item reads succeed.
5. A controlled ledger write succeeds and is visible in FFP.
6. POS authentication returns a short-lived token and protected POS endpoints reject unauthenticated requests.
7. Management authentication and authorization work.
8. A test email is accepted by `smtp.fftpj.local:25` and delivered.
9. SMTP outage/recovery exercises the durable retry queue without duplicate mail.

No Bitwarden, `BWS_ACCESS_TOKEN`, `FfpDatabaseConnectionString`, `CafeteriaServicesApiKey`, or SMTP password should be configured on the Linux Services host.
