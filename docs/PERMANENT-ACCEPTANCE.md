# Permanent Cafeteria acceptance

The isolated acceptance command is part of the actual Cafeteria Services executable.
It loads the real FFP integration plugin, uses the production application services,
and creates real Services and POS SQLite databases under a unique execution directory.
It does not start the ordinary Services host or background workers. No mock database
or mock FFP provider contributes to integration evidence.

## Commission the environment

1. Inspect the deployed Cafeteria Services configuration, environment overrides and
   working directory. Record the physical paths of **both** the central service
   SQLite store and the runtime/POS SQLite store, including any configured production
   terminal paths. Record the engineer and verification timestamp in the manifest.
   Repository defaults are not sufficient proof of deployed configuration.
2. Use either an explicitly pinned SQL login or a Windows Trusted Connection identity.
   For Windows, set `expectedWindowsIdentity` to the exact DOMAIN\user returned by
   `ORIGINAL_LOGIN()` and leave `expectedSqlLogin` empty. For SQL authentication, set
   `expectedSqlLogin` and leave `expectedWindowsIdentity` empty. The selected identity
   must have no access to **FFPJM**, no administrative/control/alter privileges, and
   enough metadata visibility to verify dependency isolation.
   Acceptance needs SELECT on Employees, Holidays, LunchItems and EmployeeIDScans;
   INSERT on EmployeeIDScans; and narrowly authorized UPDATE of the explicitly selected
   employee eligibility fixtures. Authentication choice does not bypass these checks.
3. Explicitly select at least two employee fixture IDs in the **FFP test database**.
   They must be active, eligible and employed before the selected completed month.
   The default `fixtureCodePrefix: "CAF-ACCEPT-"` also checks dedicated fixture codes.
   If existing test employee IDs are expressly designated for acceptance, use an empty
   `fixtureCodePrefix` to rely on the explicit ID allowlist without renaming their codes.
   Missing IDs still block the run. The runner never deletes FFP data or rewrites the
   shared FFP00/holiday reference data.
4. Copy `configuration/acceptance.manifest.example.json` outside source control.
   Fill in the verified production inventory, fixture IDs, selected completed month,
   independently checked FFP00 Price/Cost, expected holidays and expected working days.
   Choose a fixture month/rates with nonzero employee-funded capacity for debt proofs.
   Pin the reviewed integration plugin binary using `integrationPluginSha256`.
   For Level 1 use `level: 1`; Level 2 uses `level: 2` and cannot qualify full integration.
5. Inject the acceptance connection through `CAFETERIA_ACCEPTANCE_FFP`. Its Initial
   Catalog must be FFP, its server and actual login identity must match the manifest, and `Enlist=false`
   is required. Do not save passwords in manifests, reports, Git or command logs.

The current fail-closed verifier rejects executable indirect paths on the provider
tables, including enabled triggers, computed columns, executable defaults, security
policies and unresolved/cross-database dependencies. If the test database depends
on a trigger, the run is **blocked**, not silently allowed. A reviewed, bounded
dependency verifier would be required to support such a database without weakening
the isolation rule. Do not disable shared test-database behavior just to get a pass.

## Local Windows configuration for Fitzroy

Copy `configuration/acceptance.manifest.fwright.example.json` outside source control.
It pins JMDB01 / FFP, Windows identity `fftpj\fwright`, employee IDs 61 and 62,
September 2026, Level 1 and clean per-run databases under
`D:\sqlite\cafeteria-acceptance`. Existing service/app configuration remains the
source of the production database inventory; record its effective paths rather than
changing the normal database configuration. Fill in the verified production Services/POS SQLite paths,
verification name/time, local plugin path and SHA256, FFP00 Price/Cost and independently
verified September holidays/working days. FFP00 was confirmed unchanged over that period.
The example is intentionally incomplete until those host-specific values are recorded.

From PowerShell running under the confirmed Windows identity:

```powershell
$env:CAFETERIA_ACCEPTANCE_FFP = 'Server=JMDB01;Database=FFP;Trusted_Connection=True;Enlist=false;Encrypt=True'
```

Use the SQL server's configured TLS policy and trusted certificate. No SQL password is
needed. Server-side `ORIGINAL_LOGIN()` must match the pinned identity; production access,
privilege, dependency and database-path checks still run before financial writes.

## Execute

Build the three repositories from `feature/cafeteria-permanent-acceptance` with the
existing sibling `Common` layout. Keep other Common dependencies on their compatible
main branches. Cafeteria is based on PR #30 merge `78ce4b3`.

```powershell
dotnet restore .\Aegis.Cafeteria.Services\Aegis.Cafeteria.Services.csproj
dotnet build .\Aegis.Cafeteria.Services\Aegis.Cafeteria.Services.csproj -c Release --no-restore
dotnet test .\Aegis.Cafeteria.Tests\Aegis.Cafeteria.UnitTests\Aegis.Cafeteria.UnitTests.csproj -c Release
dotnet test .\Aegis.Cafeteria.Tests\Aegis.Cafeteria.PersistenceTests\Aegis.Cafeteria.PersistenceTests.csproj -c Release
dotnet test .\Aegis.Cafeteria.Tests\Aegis.Cafeteria.ReportingTests\Aegis.Cafeteria.ReportingTests.csproj -c Release
.\scripts\Invoke-CafeteriaAcceptance.ps1 -ManifestPath C:\AegisAcceptance\manifest.json -Purpose ReleaseQualification
```

Linux/macOS:

```bash
bash scripts/qualify-cafeteria.sh /absolute/acceptance-manifest.json
```

For a verified deployed runner, invoke the PowerShell script with `-ServicesAssembly`
and its reviewed `-ExpectedRunnerSha256`, using `-Purpose AfterDeployment`. Exit 0
means every selected proof passed, 1 means an application assertion failed, and 2
means execution is blocked. Level 2 success does not imply Level 1 integration proof.
The supplied deployment scripts do not currently deploy Cafeteria Services, so an
automatic production deployment hook has not been added to those unrelated scripts.
The Cafeteria deployment pipeline must invoke this command after its deployment
verification and before release qualification succeeds.

## Operations configuration

Configure these non-secret Operations values on the acceptance execution host:

```json
{
  "CafeteriaAcceptance": {
    "RootDirectory": "/var/lib/aegis/cafeteria-acceptance",
    "ServicesAssembly": "/opt/aegis-cafeteria-services/Aegis.Cafeteria.Services.dll",
    "RunnerSha256": "SHA256-OF-THE-REVIEWED-ACCEPTANCE-CAPABLE-ASSEMBLY",
    "ManifestPath": "/etc/aegis-cafeteria-acceptance/manifest.json",
    "DotnetExecutable": "/usr/bin/dotnet",
    "QualificationPath": "/etc/aegis-cafeteria-acceptance/qualification.json"
  }
}
```

Operations must also receive the dedicated acceptance connection environment variable;
it is not forwarded from a browser. Its account needs access to the acceptance output
directory. An old Services binary without the isolated command is rejected before it
can be launched. The authorized action requires both `Diagnostics.Run` and
`Diagnostics.Critical`, an explicit engineering reason and a custom action header.
Only one acceptance process is accepted at a time, with a filesystem lock protecting
against concurrent CLI/deployment executions too.

Set `Acceptance:RootDirectory` in the ordinary Services configuration to enable the
read-only Common.Diagnostics acceptance-evidence check. The five existing diagnostic
levels, cadence and reporting contracts remain in place. Routine diagnostics read
evidence; they never execute the financial suite.

## Evidence and qualification

Each execution retains `acceptance.json`, Services/POS SQLite files if safety passed,
schema fingerprints, an application binary fingerprint and diagnostic expected/actual
values. Results are also saved using Common.Diagnostics' existing
`JsonEngineeringDiagnosticRunStore`. An eligibility restoration journal is flushed
before changing a dedicated fixture; unfinished journals are recovered on the next
authorized run after safety verification. No FFP financial rows are broadly cleaned up.

Operations shows history, per-proof results, timestamps, versions and diagnostic detail.
Full integration requires all twenty mandatory proofs, verified isolation and a
completed run. Failed, blocked, duplicate, missing or unexecuted proofs cannot qualify.

Development/release sign-off additionally requires an explicit qualification document
matching the report's exact `applicationVersion` (including its SHA256), with
`reviewedBy`, `reviewedAtUtc`, `buildAndTestsPassed`, `deploymentVerified` and
`releaseBlockingDefects`. No file means no development or UAT sign-off. Do not populate
those fields until the corresponding review/build/deployment evidence exists.

Payroll calculation and CSV export are exercised. The downstream payroll recipient
acknowledgement is not available in the inspected contracts; CAF-15 remains blocked
unless that integration boundary is implemented and proven. The framework does not
pretend a generated payroll file proves its delivery or import.

| Legacy function | Replacement proofs | Retirement action |
| --- | --- | --- |
| Monthly Credit Allocation | CAF-04, CAF-05, CAF-06, CAF-10, CAF-17, CAF-18 | Human decision after passing evidence |
| Stipend Allocation | CAF-08, CAF-17, CAF-18, CAF-19 | Human decision after passing evidence |
| Additional Credit | CAF-09, CAF-17, CAF-18, CAF-19 | Human decision after passing evidence |

No acceptance or diagnostic result disables a legacy function.

## Current limitations to resolve before qualification

- The deployed production inventory and local FFP test connection are not available
  in this implementation environment. Real FFP integration proofs have not executed.
- Indirect SQL dependency verification is deliberately conservative; enabled triggers
  block acceptance until a reviewed safe dependency path is supported.
- A real downstream payroll acknowledgement contract remains an implementation gap.
- Service balance synchronization, automatic debt propagation and POS-to-payroll
  reconciliation are mandatory assertions, not assumed capabilities. Inspect any
  failures produced by the live run and correct the application rather than loosening
  expected values.
- Service financial actions are invoked directly with an isolated engineering
  principal. Windows FFP Manager UI interaction and live AD authentication remain
  deployment/user acceptance checks, not claims established by these scenarios.

