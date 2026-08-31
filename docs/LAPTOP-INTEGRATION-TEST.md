# Aegis.Cafeteria v92 - Laptop Integration Test

## Purpose

This profile exercises the complete service synchronization path on one Windows development laptop without writing to the production FFP database.

The Development environment uses `DevelopmentFfpGateway`, which provides isolated test employees, products, funding rates, account summaries, and ledger publication in memory. The production service never registers this gateway.

## Components

Run these together:

1. `Aegis.Cafeteria.Services` on `http://localhost:5088`.
2. `Aegis.Cafeteria.UI` as the Windows POS.
3. The service's SQLite event store `cafeteria.services.development.db`.
4. The POS SQLite database `cafeteria.pos.development.db`.
5. SignalR at `/hubs/cafeteria`.
6. The existing durable `/api/pos/events` synchronization as the fallback.

## Test identities and catalog

The Development service exposes these fake employees:

- `900001` - Test Employee One - Code `TEST001` - NFC `TEST-NFC-001` - credit eligible.
- `900002` - Test Employee Two - Code `TEST002` - NFC `TEST-NFC-002` - credit eligible.
- `900003` - Test Cash Only - Code `TEST003` - NFC `TEST-NFC-003` - not credit eligible.

Test policy rates are intentionally non-production values:

- FFP00 Price: 500.00
- FFP00 Cost: 300.00

Test products are Test Meal, Test Drink, and Test Snack.

## Prerequisites

- Windows laptop with .NET 10 SDK.
- The solution extracted locally.
- PowerShell.
- For management API calls, run under a Windows identity that can authenticate to Negotiate. In Development the authorized-group list is empty, so any authenticated Windows identity is accepted. Production group restrictions remain unchanged.

Verify:

```powershell
 dotnet --list-sdks
```

A .NET 10 SDK must be listed.

## Build first

From the solution root:

```powershell
 dotnet clean
 dotnet restore
 dotnet build --configuration Debug
 dotnet test --configuration Debug
 python scripts\static-audit.py
```

## Start the complete laptop environment

```powershell
 .\scripts\windows\Start-LaptopIntegrationTest.ps1 -ResetData
```

The script opens two PowerShell windows:

- Cafeteria Services.
- Windows POS.

It also verifies `/health` before starting the POS.

## POS test

Sign in to the POS normally.

Find `Test Employee One` using any of:

- Name: `Test Employee One`
- Code: `TEST001`
- NFC: `TEST-NFC-001`

Leave this employee displayed.

## Trigger Additional Credit

In another PowerShell window:

```powershell
 .\scripts\windows\Test-LaptopRealtimeCredit.ps1
```

The script:

1. Sets the employee's Additional Credit Limit to 1,000.00.
2. Approves 100.00 Additional Credit using a unique operation ID.
3. Commits the immutable service event to the Services SQLite database.
4. Services broadcasts `EventsAvailable` through SignalR.
5. The POS wakes its existing durable event synchronization loop.
6. The POS requests events after its last processed sequence.
7. The POS saves the Additional Credit locally.
8. The POS raises `ICustomerAccountChangeNotifier`.
9. The Windows main form reloads the employee if currently displayed.
10. The customer-facing display is refreshed by the normal `ShowCustomer` path.

Expected result: the displayed employee's financial values change almost immediately.

## Prove polling fallback still works

Stop Cafeteria Services, leave the POS running, then restart Services. Or temporarily disable real-time in the POS Development configuration:

```json
"RealtimeEnabled": false
```

Run the Additional Credit test again. The POS must still receive and display the change within the configured `PollSeconds` interval, currently 15 seconds.

This proves SignalR is an optimization, not the accounting transport.

## Prove offline catch-up

1. Stop the POS.
2. Leave Services running.
3. Run `Test-LaptopRealtimeCredit.ps1` one or more times with amounts within the configured limit.
4. Restart the POS.
5. Display Test Employee One.

The POS must resume from the last processed service-event sequence and apply every missed durable event exactly once.

## Stipend test

A stipend can be submitted with PowerShell using Windows credentials:

```powershell
$body = @{
    customerId = 900001
    cashAmount = 200
    cardReductionAmount = 100
    paymentMode = 1
    expenseReportReference = "LAPTOP-TEST"
    notes = "Development integration test"
    operationId = [Guid]::NewGuid()
} | ConvertTo-Json

Invoke-RestMethod `
    -Uri "http://localhost:5088/api/management/stipends" `
    -Method Post `
    -UseDefaultCredentials `
    -ContentType "application/json" `
    -Body $body
```

The same SignalR-wakeup plus durable-pull sequence is used.

## Production difference

Development only:

- fake FFP gateway;
- local SQLite files;
- HTTP localhost;
- API key `development-only`;
- any authenticated Windows identity accepted by the Development management policy.

Production remains:

- real FFP SQL gateway;
- Bitwarden-backed service API key and FFP connection string;
- HTTPS;
- AD management groups;
- server-hosted `Aegis.Cafeteria.Services`;
- POS terminals connecting outbound to the service.

## v97 development FFP source

Development Services now reads real reference data from `JMDB01`, database `FFP`, using Windows Integrated Security:

```text
Data Source=JMDB01;Initial Catalog=FFP;Integrated Security=true;Pooling=false;Encrypt=false
```

The development profile also sets:

```json
"DevelopmentData": { "Enabled": false },
"FfpIntegration": {
  "Enabled": true,
  "AllowWrites": true
}
```

Therefore the laptop can test real Employees, Departments, Holidays, LunchItems, account summaries, and FFP ledger-based reports while blocking Cafeteria writes to FFP. Services and POS still use isolated development SQLite databases.

The Windows account running Services must have SQL Server read/write access to the `FFP` database on `JMDB01`.
