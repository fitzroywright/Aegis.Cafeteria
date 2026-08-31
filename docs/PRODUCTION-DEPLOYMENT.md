# Production Deployment and Configuration — v88

This is the authoritative production deployment runbook for Aegis.Cafeteria v88.

## 1. Recommended production topology

```text
Windows/Linux POS terminals
        |
        | HTTPS + X-Cafeteria-Api-Key
        v
Aegis.Cafeteria.Services (domain-joined Windows Server)
        |
        | Integrated SQL security or protected SQL connection string
        v
FFP SQL Server / FFP database

FFP Manager
        |
        | Windows Integrated Authentication (Negotiate)
        v
Aegis.Cafeteria.Services /api/management/*
```

Production rule: POS terminals should not connect directly to FFP SQL. Set `CafeteriaServices:Enabled=true` on the POS. Services owns FFP SQL connectivity, month opening, management-originated financial events, durable FFP publication and scheduled mail.

## 2. Production prerequisites

- .NET 10 runtime, or publish self-contained.
- Domain-joined Windows Server for `Aegis.Cafeteria.Services` if Windows/AD management authentication is required.
- Network access from Services to FFP SQL Server.
- LDAPS access from POS terminals to `jmad01.fftpj.local` / `jmad02.fftpj.local` on TCP 636.
- HTTPS access from POS and FFP Manager to Cafeteria Services.
- Bitwarden Secrets Manager enabled in the self-hosted Bitwarden Enterprise organization.
- `bws` CLI installed on Services and on each POS terminal that resolves secrets locally.
- A dedicated AD service account for Cafeteria Services is recommended, for example `FFTPJ\svcAegisCafeteria`.

## 3. AD groups

Create/verify these groups:

- `Cafeteria Managers` — approve Additional Credit, set per-employee Additional Credit Limit, run manual month opening, post stipends.
- `Cafeteria Administrators` — same or broader administrative access.
- `Domain Admins` — emergency administrative access only.

The configured groups are enforced by both POS manager authorization and Services management API authorization.

## 4. Bitwarden Secrets Manager

### 4.1 Create a project

Create a Secrets Manager project named `Aegis.Cafeteria.Production`.

Create these secrets:

| Logical name | Required by | Value |
|---|---|---|
| `FfpDatabaseConnectionString` | Services | Production FFP SQL connection string |
| `CafeteriaServicesApiKey` | Services + POS | Strong random service-to-service API key |
| `MessagingBearerToken` | POS only if Messaging is enabled | Messaging API bearer token |

Do not store these values in `appsettings.json`.

### 4.2 Machine accounts

Recommended separation:

- `Aegis-Cafeteria-Services-PROD` — read access to `FfpDatabaseConnectionString` and `CafeteriaServicesApiKey`.
- `Aegis-Cafeteria-POS-PROD` — read access to `CafeteriaServicesApiKey` and, only if required, `MessagingBearerToken`.

The POS machine account does not need the FFP SQL secret when Services mode is enabled.

Create one access token for each machine account. Treat the token as a bootstrap credential. The application reads it only from the environment variable named by `SecretsManager:AccessTokenEnvironmentVariable` (default `BWS_ACCESS_TOKEN`).

### 4.3 Install `bws`

Install the Bitwarden Secrets Manager CLI and put `bws`/`bws.exe` on PATH for the account that runs the application/service.

Verify:

```powershell
bws --version
```

For self-hosted Bitwarden, either set `SecretsManager:ServerUrl` in appsettings or configure `bws` server-base. The application supports the `--server-url` option directly.

### 4.4 Configure the machine token

For the Windows service account, create a machine-level environment variable while running as administrator:

```powershell
[Environment]::SetEnvironmentVariable(
  "BWS_ACCESS_TOKEN",
  "<access-token>",
  "Machine")
```

Restart the Windows service after changing the variable.

For a dedicated Linux POS service/user, configure `BWS_ACCESS_TOKEN` in the systemd unit environment or a root-owned EnvironmentFile with restrictive permissions.

Never put the token in source control, command-line arguments, logs or appsettings.

### 4.5 Record secret UUIDs

Run using the appropriate machine token:

```powershell
bws secret list --output table
```

Record the UUID of each secret. Put only the UUIDs in `SecretsManager:SecretIds`.

Example Services configuration:

```json
"SecretsManager": {
  "CliPath": "bws",
  "AccessTokenEnvironmentVariable": "BWS_ACCESS_TOKEN",
  "ServerUrl": "https://<your-bitwarden-host>",
  "CommandTimeoutSeconds": 15,
  "SecretIds": {
    "FfpDatabaseConnectionString": "<uuid>",
    "CafeteriaServicesApiKey": "<uuid>"
  }
}
```

Example POS configuration:

```json
"SecretsManager": {
  "CliPath": "bws",
  "AccessTokenEnvironmentVariable": "BWS_ACCESS_TOKEN",
  "ServerUrl": "https://<your-bitwarden-host>",
  "CommandTimeoutSeconds": 15,
  "SecretIds": {
    "CafeteriaServicesApiKey": "<uuid>",
    "MessagingBearerToken": ""
  }
}
```

### 4.6 Validate Bitwarden before application startup

```powershell
bws secret get <ffp-secret-uuid> --output json
bws secret get <api-key-secret-uuid> --output json
```

Run these commands as the actual service/application identity or in an equivalent environment. v88 fails closed in non-Development environments if Services mode is enabled but `CafeteriaServicesApiKey` cannot be resolved.

## 5. FFP SQL permissions

If the FFP connection string uses Integrated Security, grant the Cafeteria Services service account only the permissions required by the current adapter.

Required reads:

- `dbo.Employees`
- `dbo.Departments`
- `dbo.Holidays`
- `dbo.LunchItems`
- `dbo.EmployeeIDScans`

Required writes:

- `INSERT` on `dbo.EmployeeIDScans`
- `UPDATE` on `dbo.Employees` for the compatibility projection fields `CreditLimit` and `AvailableBalance`

No DELETE permission is required.

Example SQL, adjust database/user names to FFP standards:

```sql
CREATE USER [FFTPJ\svcAegisCafeteria] FOR LOGIN [FFTPJ\svcAegisCafeteria];
GRANT SELECT ON dbo.Employees TO [FFTPJ\svcAegisCafeteria];
GRANT SELECT ON dbo.Departments TO [FFTPJ\svcAegisCafeteria];
GRANT SELECT ON dbo.Holidays TO [FFTPJ\svcAegisCafeteria];
GRANT SELECT ON dbo.LunchItems TO [FFTPJ\svcAegisCafeteria];
GRANT SELECT, INSERT ON dbo.EmployeeIDScans TO [FFTPJ\svcAegisCafeteria];
GRANT UPDATE ON dbo.Employees TO [FFTPJ\svcAegisCafeteria];
```

If you want stricter column-level UPDATE permissions, grant UPDATE only on `CreditLimit` and `AvailableBalance`.

## 6. FFP data prerequisites

Verify before go-live:

- `Employees.ID`, `Code`, `TRN`, `NFCID`, `FirstName`, `LastName`, `Photo`, `DepartmentID`, `EligibleForLunchTicket` exist and are populated appropriately.
- `Departments.ID/Name` resolve department names.
- `Holidays.CalendarDate` contains non-working dates.
- `LunchItems` contains active sale products.
- The configured credit policy row exists and has a positive `Price`.
- The configured subsidy policy row exists and has a non-negative `Cost`.
- `EmployeeIDScans.TransmissionID` can store the operation GUID text and should be indexed/unique if operationally safe.
- `Employees.CreditLimit` and `Employees.AvailableBalance` exist.

Default policy configuration is `FFP00` for both:

- Monthly Credit Limit = eligible working days × `FFP00.Price`
- Monthly Subsidy = min(Credit Limit, eligible working days × `FFP00.Cost`)

## 7. Configure Aegis.Cafeteria.Services

Recommended production settings:

```json
{
  "urls": "https://0.0.0.0:5088",
  "FfpIntegration": {
    "Enabled": true,
    "ConnectionString": "",
    "CreditLimitLunchItemCode": "FFP00",
    "SubsidyLunchItemCode": "FFP00",
    "MonthlyAllocationScanType": 3,
    "PurchaseScanType": 9,
    "RefundScanType": 9,
    "AdjustmentScanType": 9
  },
  "ServicePersistence": {
    "ConnectionString": "Data Source=C:\\ProgramData\\Aegis.Cafeteria.Services\\cafeteria.services.db"
  },
  "ManagementApi": {
    "ApiKey": "",
    "AuthorizedGroups": [
      "Cafeteria Managers",
      "Cafeteria Administrators",
      "Domain Admins"
    ]
  },
  "MonthOpening": {
    "Enabled": true,
    "RunHourLocal": 1
  }
}
```

The production connection string and Services API key are resolved from Bitwarden when their plaintext settings are blank.

### HTTPS

Do not expose production Services over plain HTTP. Terminate TLS either directly in Kestrel or at an internal reverse proxy. FFP Manager and POS clients should use an internal DNS name such as `https://cafeteria-services.admin.ffpja.org` or the approved internal equivalent.

### Windows Integrated Authentication

Management endpoints use ASP.NET Core Negotiate authentication. If Services runs under a domain service account and you use a DNS alias, configure the HTTP SPN for that alias against the service identity. Test from a domain workstation with the exact URL FFP Manager will use.

## 8. Install Services as a Windows service

Publish:

```powershell
dotnet publish .\src\Aegis.Cafeteria.Services\Aegis.Cafeteria.Services.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o C:\Apps\Aegis.Cafeteria.Services
```

Create a service using the approved service account. Example:

```powershell
sc.exe create Aegis.Cafeteria.Services `
  binPath= "C:\Apps\Aegis.Cafeteria.Services\Aegis.Cafeteria.Services.exe" `
  start= auto `
  obj= "FFTPJ\svcAegisCafeteria" `
  password= "<set securely>"
```

Then:

```powershell
sc.exe failure Aegis.Cafeteria.Services reset= 86400 actions= restart/60000/restart/60000/restart/60000
Start-Service Aegis.Cafeteria.Services
Get-Service Aegis.Cafeteria.Services
```

Do not embed the service-account password in deployment scripts kept in source control.

## 9. Configure Windows POS

Use a unique terminal ID per terminal.

Recommended production settings:

```json
"Persistence": {
  "LocalSqliteConnectionString": "Data Source=C:\\ProgramData\\Aegis.Cafeteria\\cafeteria.local.db;Cache=Shared",
  "TerminalId": "CAFETERIA-POS-01",
  "FfpPublicationBatchSize": 100,
  "FfpPoisonMessageAttemptLimit": 25
},
"FfpIntegration": {
  "Enabled": true,
  "ConnectionString": "",
  "RefreshIntervalMinutes": 10,
  "CreditLimitLunchItemCode": "FFP00",
  "SubsidyLunchItemCode": "FFP00"
},
"CafeteriaServices": {
  "Enabled": true,
  "BaseUrl": "https://cafeteria-services.admin.ffpja.org",
  "PollSeconds": 15,
  "UseServiceMonthlyAllocations": true,
  "ApiKey": ""
}
```

`ApiKey` remains blank because it is resolved from Bitwarden.

Verify:

- local SQLite directory writable by the POS user;
- health heartbeat directory writable;
- LDAPS certificate chain trusted;
- second display detected if used;
- product and employee caches refresh successfully through Services;
- POS can complete a sale while Services/FFP is temporarily unavailable, and later publish from the outbox.

## 10. Configure Linux POS

Equivalent settings apply. Default paths:

- database: `/var/lib/aegis-cafeteria/cafeteria.local.db`
- heartbeat: `/var/lib/aegis-cafeteria/health/heartbeat.json`

Create and permission the directory:

```bash
sudo install -d -m 0750 -o <pos-user> -g <pos-group> /var/lib/aegis-cafeteria
sudo install -d -m 0750 -o <pos-user> -g <pos-group> /var/lib/aegis-cafeteria/health
```

Configure `BWS_ACCESS_TOKEN` for the POS user using the deployment mechanism approved for the Linux host.

## 11. Local database deployment

The POS database remains local-first. Schema baseline is 73. For a new production terminal, start with no existing `cafeteria.local.db`; initialization creates the database. Do not copy a development database into production.

The Services database is separate and stores durable management events, publication state and employee Additional Credit policy.

Back up both SQLite databases according to the operational backup policy.

## 12. Month-opening cutover

Before enabling automatic month opening:

1. Verify `FFP00.Price` and `FFP00.Cost` (or configured policy codes).
2. Verify current-month holidays.
3. Verify `EligibleForLunchTicket` values.
4. Start Services.
5. Run a manual dry operational test for a non-production/test employee if available.
6. Execute `POST /api/month-opening/run?year=YYYY&month=M` as an authorized manager.
7. Verify Services event store, FFP `EmployeeIDScans`, `Employees.CreditLimit`, `Employees.AvailableBalance`, and POS event synchronization.
8. Only then leave `MonthOpening:Enabled=true` for automatic execution.

Operation IDs are deterministic, so rerunning the same employee/month does not create a second allocation event.

## 13. Production verification checklist

- `/health` returns running.
- Unauthenticated call to `/api/management/...` is rejected.
- Authorized Cafeteria Manager can GET/PUT employee credit policy.
- Authorized Cafeteria Manager can approve Additional Credit within cap.
- Approval above the employee cap is rejected.
- Additional Credit appears in Services event store and FFP ledger.
- POS receives Additional Credit event and refreshes Available to Spend.
- Stipend card reduction changes FFP available balance; cash-only stipend does not.
- Month opening publishes exactly once.
- Cash Customer sale never writes employee ledger data.
- Customer Payment reduces Employee Amount Due.
- Refund/void behavior matches audit rules.
- Reports preview and XLSX/PDF export work without freezing the UI.
- Graylog/health heartbeat operate as configured.

## 14. Build/release gate

Run on the build machine:

```powershell
dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
python scripts\static-audit.py
```

Do not deploy if any command fails.

## v92 SignalR production connectivity

The POS initiates an outbound HTTPS/SignalR connection to the same Cafeteria Services host used for REST integration. Permit POS-to-Services TCP 443. No Services-to-POS initiated firewall rule is required.

Keep these production POS settings enabled:

```json
"CafeteriaServices": {
  "Enabled": true,
  "RealtimeEnabled": true,
  "HubPath": "/hubs/cafeteria",
  "RealtimeReconnectSeconds": 5,
  "PollSeconds": 15
}
```

`CafeteriaServicesApiKey` protects both the REST POS endpoints and the SignalR hub. The POS sends it as `X-Cafeteria-Api-Key` during SignalR negotiation/connection.

Do not disable polling when SignalR is enabled. Polling is the recovery mechanism for missed notifications, service restarts, network interruptions, and POS offline periods.

## Configure the v93 funding policy before first month opening

`FFP00` is no longer the production Services source for cafeteria funding rates. Before the first production month opening, configure the Services funding policy.

For a brand-new Services database, temporarily set the bootstrap values in `Aegis.Cafeteria.Services/appsettings.Production.json` or equivalent environment-specific configuration:

```json
"FundingPolicy": {
  "DailyCreditLimitRate": 0.00,
  "DailyCompanySubsidyRate": 0.00
}
```

Replace both zero values with the approved production rates before first startup. Services writes them to its own audited funding-policy tables. After the policy exists in the Services database, maintain it through FFP Manager using the Services management API. Services mirrors the approved values to `FFP00` for compatibility; do not edit `FFP00` directly.

Do not run Month Opening until both rates have been verified.

## Price maintenance

Authorized managers use POS `Administration -> Funding Policy & Price Maintenance` to edit the active FFP LunchItems catalog. Product code is immutable; product Name, Price and Cost are editable. Changes are written by Services to FFP SQL and then pushed to POS terminals through a catalog-change notification. The periodic catalog refresh remains enabled as a recovery path.


## v98 product and workforce boundary

Only employees with `FFP.Employees.EligibleForLunchTicket = 1` are active Cafeteria employees. The POS does not load ineligible employees for browse, name search or identifier matching. Historical customer rows may remain locally only to support old transaction history.

Saleable cafeteria products are owned by the POS SQLite database. `FFP.dbo.LunchItems` and Cafeteria Services are not product-catalog sources. Product administration changes the current POS catalog only; completed order lines preserve ProductName, UnitPrice and Quantity as immutable transaction snapshots. Reports use those transaction/ledger values rather than looking up today's product price.

`FFP00` is retained solely as a compatibility mirror for the Services-owned funding policy and must not be treated as a saleable product or catalog source.
