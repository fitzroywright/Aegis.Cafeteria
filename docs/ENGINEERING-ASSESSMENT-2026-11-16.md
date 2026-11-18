# Aegis.Cafeteria engineering assessment � 2026-11-16

Assessment and safe local repairs completed in `D:\Projects\Aegis.Cafeteria`. No commit, push, merge, remote connection, deployment, or production configuration change was performed. Existing databases were opened only by the read-only inspection utility; application initializers ran only against test-created temporary databases. The pre-existing `cafeteria-services.tar.gz` was left untouched.

## Confirmed findings and repairs

| Finding | Evidence and consequence | Local repair |
|---|---|---|
| Framework drift and unpinned SDK | At entry, all 13 Cafeteria projects targeted .NET 11 and Microsoft runtime references had been upgraded. Eight required Common projects also targeted .NET 11. Both SDK 10.0.403 and 11.0.100 are installed; without `global.json`, the default was 11.0.100. | Restored Cafeteria and those eight Common projects to .NET 10; pinned SDK major 10 with feature-band roll-forward in `global.json:1`. Cafeteria Microsoft runtime/EF/SQLite references use 10.0.15. Common Microsoft references use 10.0.12, compatible with the consuming patch versions. Unrelated pre-existing package upgrades were retained. |
| Broken Migration plugin staging | Entry-state Migration targeted net11.0 but its staging source still named `bin/<Configuration>/net10.0`. The baseline build failed with �Cafeteria integration plugin was not staged�. Checking an existing destination could also mask stale binaries. | `build/Cafeteria.PluginStaging.targets:3` asks plugin projects for `GetTargetPath`, checks source existence, and stages their output trees for build and publish. The target explicitly uses net10.0 and removes the host's TFM/RID/self-contained properties when querying portable library outputs. Used by Services, Windows UI, and Migration. |
| Missing Windows publish staging | Windows UI had an authentication-plugin target only after Build. A fresh publish folder was not guaranteed to receive the plugin. | The shared staging target now runs after Publish as well (`build/Cafeteria.PluginStaging.targets:19`). Both local publish packages contain their required plugins. |
| POS launch-directory dependency and uncaught configuration failures | Windows `Program` constructed the default host and loaded mandatory relative JSON files before the startup exception handler. Starting from a shortcut, shell, or different working directory could select another configuration directory or fail outside the error dialog. | Windows host construction now occurs inside the startup handler and uses `AppContext.BaseDirectory` as content root (`Aegis.Cafeteria.UI/Program.cs:35`). Linux POS uses the same content-root policy (`Aegis.Cafeteria.UI.Linux/LinuxCafeteriaHost.cs:24`). |
| Relative SQLite paths were not anchored | POS passed relative strings directly to SQLite. Services created the parent using `GetFullPath` but returned the original relative connection string. Database selection therefore depended on process CWD. | `SqliteDatabasePath.Resolve` canonicalizes ordinary filesystem data sources without opening or creating a database (`Aegis.Cafeteria.Infrastructure/Configuration/SqliteDatabasePath.cs:7`). POS resolves against the executable directory (`CafeteriaRuntimeOptionsResolver.cs:95`); Services resolves against its configured content root (`Aegis.Cafeteria.Services/Hosting/CafeteriaServiceDatabase.cs:10`). Absolute paths remain absolute. SQLite memory databases and `file:` URIs retain their native semantics. |
| Fragile SQLite directory parsing | `RuntimePathInitializer` split on semicolons and recognized only literal `Data Source=`. It mishandled quoted semicolons and SQLite aliases such as `Filename`. | It now uses `SqliteConnectionStringBuilder` (`RuntimePathInitializer.cs:16`). Parent creation does not create a database. URI handling is left to SQLite. |
| Stale Visual Studio launch entries | `Aegis.Cafeteria.slnLaunch` referred to `Aegis.Cafeteria.Management.csproj`, which does not exist; that directory contains only old bin/obj outputs. | Removed the nonexistent Management entry from the combined profile and removed Management Only. All remaining launch project paths exist locally. No sibling control-plane application was modified. |

No integration plugin loader change was necessary. `Aegis.Cafeteria.Services/Hosting/IntegrationPluginLoader.cs:35` resolves an absolute configured path directly, otherwise prefers the content root and falls back to the application base. It requires exactly one public implementation for each of six integration contracts. Services explicitly references SqlClient, so its runtime dependencies are in the host graph. The existing authentication loader also uses an assembly dependency resolver. Existing plugin runtime-asset staging changes were retained.

## Solution and startup structure

The solution contains Domain, Application, Infrastructure, Integration Plugin, Reporting, Presentation, Windows POS (WinForms), Linux POS (Avalonia), Services (ASP.NET Core), Migration (SQL-source import CLI), and three test projects. Common.Security, Diagnostics, Secrets, Messaging, Storage, Registration, Messaging.Hosting and the AD plugin are directly required local dependencies. The missing historical Management project is not in the solution.

Windows POS configures local persistence, authentication, the UI and background synchronization. It initializes local storage before operator sign-in; background host startup is already off the UI thread. Services configures its service database and integration plugin, initializes storage before `RunAsync`, and hosts central workers and authenticated endpoints. Migration reads a supplied SQL Server connection and submits import payloads to Services; it is not another SQLite runtime.

POS configuration is base `appsettings.json`, optional `appsettings.<DOTNET_ENVIRONMENT>.json`, then environment overrides. Direct execution without an environment setting uses Production; Visual Studio / `dotnet run` launch profiles select Development. Launch profiles are tooling configuration and are not automatically applied when running a published executable. POS JSON now comes from the executable directory. Linux POS still re-adds JSON/environment providers after the default builder; command-line application options can therefore be superseded by those later providers.

Services uses `WebApplication.CreateBuilder(args)`: default host environment/content-root configuration and JSON are followed by its normal configuration providers. `CafeteriaServicesHost.Configure` additionally re-adds environment variables, giving them precedence over command-line application settings. Both DOTNET_ENVIRONMENT and ASPNETCORE_ENVIRONMENT are set to Development by the local Services launch profile. The repository systemd unit explicitly selects Production. Windows service integration uses `UseWindowsService`; SCM execution was not exercised.

## Exact database selection

These are separate stores with different schemas and responsibilities. They must not be copied over each other or expected to have identical data.

| Runtime/configuration | Selected file after repair |
|---|---|
| Windows POS Development, Debug build, no connection override | `D:\Projects\Aegis.Cafeteria\Aegis.Cafeteria.UI\bin\Debug\net10.0-windows\cafeteria.pos.development.db` |
| Windows POS Development, Release build | Corresponding `bin\Release\net10.0-windows\cafeteria.pos.development.db`; this is a separate existing, empty store. |
| Windows POS base/Production | `C:\ProgramData\Aegis.Cafeteria\cafeteria.local.db`, from UI `appsettings.json:17`, unless an environment/deployment override supplies another path. Not opened during this assessment. |
| Services Development via its project launch profile | `D:\Projects\Aegis.Cafeteria\Aegis.Cafeteria.Services\cafeteria.services.development.db`, relative to the project content root recorded in historical startup logs. |
| Services base/Production without overrides | `<content-root>/cafeteria.services.db`, from `ServicePersistence:ConnectionString`. |
| Services under the repository's Debian unit | `/var/lib/aegis-cafeteria-services/cafeteria.services.db`, from the explicit systemd environment setting at `deploy/linux/aegis-cafeteria-services.service:21`. This is the repository's intended selection, not a verified observation of Kratos. |
| Linux POS base/Production | `/var/lib/aegis-cafeteria/cafeteria.local.db`. |
| Linux POS Development | `<executable-directory>/cafeteria.pos.linux.development.db`. |
| EF design-time factory | `cafeteria.design.db` relative to its tooling process CWD; not used by application startup and not opened. |
| Services diagnostics | Separate SQLite diagnostic store, default configured as `diagnostics/diagnostic-level.db`; not the service business database. |

The Services base JSON also contains `Persistence:LocalSqliteConnectionString=Data Source=cafeteria.services.runtime.db`. Normal Services startup does not register POS persistence or consume this setting. The acceptance runner explicitly supplies its own temporary `runtime.sqlite`. `ServicePersistence:ConnectionString` is the business-store setting that matters for Services.

Read-only inspection, including WAL-visible data:

| Existing file | Main bytes | WAL bytes | Findings |
|---|---:|---:|---|
| Debug POS Development | 82,440,192 | 8,198,832 | Schema version 74; 23 application tables; 107 customers; 1 order; `quick_check=ok`. |
| Services Development | 638,976 | 4,144,752 | 12 application tables, including service events, audit, integration cache, outbound email and policy caches; 2 validated policies; 0 integration employee cache rows; `quick_check=ok`. |
| Release POS Development | 274,432 | 0 | Schema version 74; 0 customers; 0 orders; `quick_check=ok`. |

These measured sizes differ from the earlier approximately 210 MB and 4 KB observations. WAL mode makes main-file size alone an unreliable indication of initialization or logical data volume. No checkpoint was run. The inspection tool opens with `Mode=ReadOnly`, disables pooling, enables query-only behavior, and verifies the main-file SHA256 before/after; all three main files were unchanged. This is an integrity/schema inventory, not a complete financial reconciliation or an atomic backup.

## Initialization and migrations

`Aegis.Cafeteria.Infrastructure/Persistence/PersistenceInitializer.cs:19` creates schema only for a database without user tables, then records schema 74. Existing stores run the narrowly defined 73-to-74 upgrade and schema validation, with additive cache/identification-column handling. Unsupported financial schemas are rejected. It configures WAL and durability pragmas. The inspected existing POS stores already report 74. No initializer or upgrade was run on them.

`Aegis.Cafeteria.Services/Hosting/CafeteriaServiceDatabase.cs:24` uses EF `EnsureCreatedAsync` followed by additive `CREATE TABLE/INDEX IF NOT EXISTS` supplemental schema. It does not run EF migrations. Repeated initialization was tested twice against temporary Services databases with a sentinel row retained. Existing persistence tests exercise local creation, schema rejection and supported upgrades using disposable stores.

There is no evidence here of an existing database initialization failure. `quick_check` does not prove full EF-model compatibility. `EnsureCreated` does not upgrade arbitrary partial/old Services schemas, so a partially created database can still need a separately reviewed upgrade. Existing POS error messages and older documentation can suggest deleting/replacing legacy files; those instructions were not followed and should not be used as a recovery procedure for these stores.

## Verification and evidence

| Check | Result |
|---|---|
| Baseline build | Failed at Migration plugin staging. Initial sandbox run additionally could not write external Common build outputs; authorized rerun isolated the staging error. |
| Baseline tests from successfully built outputs | 270 unit + 72 persistence + 20 reporting = 362 passed. Some baseline startup tests hard-code net10.0 plugin paths even while test assemblies targeted net11.0, so this did not validate coherent net11 plugin staging. |
| Final SDK | 10.0.403, selected by `global.json`. |
| Final solution build | Passed, 0 errors. 36 warnings reported including repeated restore/build transitive security-advisory warnings. |
| Final tests | 275 unit + 79 persistence + 20 reporting = 374 passed; 0 failures. |
| New coverage | Explicit database base paths and aliases, memory/URI preservation, quoted semicolon paths, no file creation during path resolution, Services initialization preserving existing sentinel data, plugin content-root precedence/fallback and missing-plugin errors. |
| Windows Release publish | Passed; net10 runtime metadata; authentication plugin and dependency files present. |
| Debian linux-x64 Release publish | Passed after exposing and fixing the RID propagation issue in staging. Contains integration and authentication plugins. Cross-published on Windows; not executed on Debian. |
| Publish hygiene | Both fresh output roots contain no `.db`, WAL or `.sqlite` files. Required plugin SHA256 values recorded in the manifest. |
| Launch profiles | JSON parses; all four remaining profiles reference existing project paths. |
| Patch whitespace | `git diff --check` passed (Git issued line-ending normalization notices only). |

Logs and manifests are in ignored `artifacts/assessment/`: `assessment-baseline-build.log` is at repository root; `build-final.log`, `restore-final.log`, `tests-final.log`, `final/*.trx`, `database-inspection.log`, `publish-windows.log`, `publish-debian.log`, and `publish-manifest.json` hold detailed evidence. Intermediate logs document failed attempts as well. Final publish directories are `artifacts/assessment/publish/windows` and `artifacts/assessment/publish/debian`.

Three FFP contract tests return early when `FFPMANAGER_TEST` is absent. It was absent, so their reported passes do not represent live SQL Server contract verification. No live AD/Kerberos, SQL Server, production HTTP/realtime authentication, Windows SCM, physical POS peripheral, graphical sign-in, or Debian execution test was performed. Launching the default development applications would contact configured production endpoints, so verification used isolated test hosts instead.

## Modified source files and external dependencies

- `global.json`; `Aegis.Cafeteria.slnLaunch`; `build/Cafeteria.PluginStaging.targets`.
- Project files for Application, Infrastructure, Migration, Integration Plugin, Services, Windows UI, Linux UI, and the three test projects. Domain, Presentation and Reporting now match their original net10 targets and therefore have no net change against Git HEAD. Existing unrelated SqlClient, Avalonia and test-runner changes were retained.
- `Aegis.Cafeteria.Infrastructure/Configuration/SqliteDatabasePath.cs`, `CafeteriaRuntimeOptionsResolver.cs`, `RuntimePathInitializer.cs`.
- `Aegis.Cafeteria.UI/Program.cs`; `Aegis.Cafeteria.UI.Linux/LinuxCafeteriaHost.cs`.
- `Aegis.Cafeteria.Services/Hosting/CafeteriaServiceDatabase.cs`, `CafeteriaServicesHost.cs`.
- New `SqliteDatabasePathTests.cs`, `ServiceDatabaseStartupTests.cs`, `IntegrationPluginDiscoveryTests.cs`; updated `Secrets/CafeteriaRuntimeOptionsResolverTests.cs`.
- `tools/DatabaseInspection/DatabaseInspection.csproj` and `Program.cs`; this report.

The eight required Common project files changed outside this Git repository are under `D:\Projects\Common`: Common.Diagnostics, Common.Security, Common.Security.Plugin.ActiveDirectory, Common.Secrets, Common.Registration, Common.Messaging, Common.Messaging.Hosting, and Common.Storage. Only net11 target declarations and Microsoft/runtime package references requiring alignment were changed. Original project-file contents are preserved in `artifacts/assessment/common-before-*.csproj`; the exact executed alignment script is retained alongside them. These changes need independent review/versioning in their owning repositories before a reproducible checkout elsewhere can build. A Cafeteria-only commit would not contain them. No unrelated Common test project was retargeted.

## Remaining risks and Kratos checks

1. SqlClient 7.1.1, already present on entry, resolves `System.Security.Cryptography.Pkcs` 9.0.18. NuGet reports moderate/high advisories GHSA-2fmv-rv2c-59x7 and GHSA-846f-f764-76mx. This remains unresolved; select and verify a compatible patched dependency set before production rollout. The .NET 10 authentication/SignalR advisories raised by 10.0.12 are absent from the final 10.0.15 build. Microsoft publishes the selected patch in its [.NET 10.0.15 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.15/10.0.15.md).
2. Confirm the actual Kratos unit, drop-ins, environment file, process working directory, runtime version and database setting. The checked-in unit is not evidence of the deployed unit. Do not print secrets when collecting environment information.
3. The checked-in hardened unit permits writes under `/var/lib/aegis-cafeteria-services` and `/run/aegis-cafeteria-services`, but base diagnostics uses a relative path under the application directory. Registration defaults elsewhere use `/var/lib/aegis/cafeteria-services`. Logs also need an explicitly writable destination. These paths can fail under `ProtectSystem=strict`. Verify deployed overrides and ownership; correct production settings only after authorization.
4. Verify `aegis-cafeteria` can read the published plugins and write the existing business/diagnostic databases and their WAL/SHM directories. Check Kerberos principal, keytab access, ticket refresh, DNS and JMDB01/FFP reachability under the service identity. Check HTTPS proxy/authentication and realtime routing from POS clients.
5. The Services startup filter and explicit pipeline both register logging/audit middleware (`CafeteriaAuthenticationHostingStartup.cs:33`, `CafeteriaRuntimeStartupFilter.cs:23`, `Hosting/CafeteriaServicesPipeline.cs:15`). Historical October 15 logs contain duplicate startup lines, consistent with duplicate logging registration. This was recorded but not changed; audit/logging ownership needs a dedicated regression test before removal.
6. Debug and Release POS Development stores are demonstrably different. Pin an explicit absolute database path for an installed terminal rather than relying on build-output persistence. The repair deliberately performs no automatic search, relocation, merger or copying of a database.
7. POS configuration registration still performs best-effort remote work synchronously during configuration resolution; an unavailable endpoint can delay startup. Offline startup and sign-in against the actual Windows installation remain to be exercised with approved endpoint settings.
8. SQLite `file:` URI paths intentionally retain SQLite's own resolution semantics; only ordinary filesystem data sources are anchored by the new resolver. Avoid relative URI data sources in deployment configuration.

Read-only commands for the operator to run on Kratos after authorizing access:

```sh
systemctl cat aegis-cafeteria-services
systemctl show aegis-cafeteria-services -p User -p Group -p WorkingDirectory -p ExecStart -p FragmentPath -p DropInPaths
journalctl -u aegis-cafeteria-services -n 150 --no-pager
dotnet --list-runtimes
sudo -u aegis-cafeteria test -r /opt/aegis-cafeteria-services/plugins/Aegis.Cafeteria.Plugin.Integration.dll
sudo -u aegis-cafeteria test -w /var/lib/aegis-cafeteria-services
```

Review logs/unit contents locally for secrets before sharing. Inspect only the necessary path/environment keys, not a full environment dump. Obtain a consistent backup including WAL state through the site's established backup procedure before any approved deployment; do not copy only a live SQLite main file.

## Build and deployment commands � review only

No commands in this deployment section were run against an installed application or Kratos. Confirm the Common changes above are present first. The Linux package assumes Kratos is x64; verify architecture before using it.

Build and prepare clean local artifacts in PowerShell:

```powershell
Set-Location D:\Projects\Aegis.Cafeteria
dotnet --version
dotnet restore Aegis.Cafeteria.slnx
dotnet build Aegis.Cafeteria.slnx --no-restore
dotnet test Aegis.Cafeteria.slnx --no-build --no-restore
# Use a fresh output directory for each candidate.
dotnet publish Aegis.Cafeteria.UI/Aegis.Cafeteria.UI.csproj -c Release --self-contained false -o artifacts/release-candidate/windows
dotnet publish Aegis.Cafeteria.Services/Aegis.Cafeteria.Services.csproj -c Release -r linux-x64 --self-contained false -o artifacts/release-candidate/debian
tar -czf artifacts/release-candidate/cafeteria-services-linux-x64.tar.gz --exclude='appsettings*.json' -C artifacts/release-candidate/debian .
```

Windows deployment, only after approval, approved backup and application shutdown:

```powershell
$releaseDir = 'C:\Aegis\Cafeteria\releases\2026-11-16-reviewed'
$existingDatabase = 'REPLACE_WITH_CONFIRMED_EXISTING_ABSOLUTE_DATABASE_PATH'
$approvedConfigDirectory = 'REPLACE_WITH_APPROVED_EXISTING_CONFIGURATION_DIRECTORY'
if (!(Test-Path -LiteralPath $existingDatabase -PathType Leaf)) { throw 'Existing database must be confirmed first.' }
if (!(Test-Path -LiteralPath (Join-Path $approvedConfigDirectory 'appsettings.json') -PathType Leaf)) { throw 'Approved base configuration is required.' }
if (Test-Path -LiteralPath $releaseDir) { throw 'Choose a new empty release directory.' }
New-Item -ItemType Directory -Path $releaseDir | Out-Null
Get-ChildItem artifacts/release-candidate/windows | Where-Object { $_.Name -notlike 'appsettings*.json' } | Copy-Item -Destination $releaseDir -Recurse
Get-ChildItem -LiteralPath $approvedConfigDirectory -Filter 'appsettings*.json' | Copy-Item -Destination $releaseDir
$env:DOTNET_ENVIRONMENT = 'Production'
$env:Persistence__LocalSqliteConnectionString = "Data Source=$existingDatabase;Cache=Shared"
# Start only after reviewing connection endpoints and the existing database path.
& (Join-Path $releaseDir 'Aegis.Cafeteria.UI.exe')
```

This uses a new application folder and explicitly selects the existing database; it copies no database. Update the approved terminal shortcut to the new executable after smoke testing. Keep the previous application folder available, but do not assume schema-changing releases can be rolled back without a recovery review.

Debian preparation and deployment template, only after explicit Kratos/deployment approval:

```sh
# Transfer the candidate only after access/deployment approval.
scp artifacts/release-candidate/cafeteria-services-linux-x64.tar.gz Kratos:/tmp/cafeteria-services-reviewed.tar.gz
# Run the following on Kratos after validating the actual service layout.
release=/opt/aegis-cafeteria-services/releases/2026-11-16-reviewed
sudo test ! -e "$release" || exit 1
sudo install -d -m 0755 "$release"
sudo tar --keep-old-files -xzf /tmp/cafeteria-services-reviewed.tar.gz -C "$release"
# Copy the approved existing JSON configuration into the new release, not over the original.
sudo cp -p /opt/aegis-cafeteria-services/appsettings.json "$release/appsettings.json"
# If used, also copy the reviewed existing appsettings.Production.json into this new release.
sudo test -f /var/lib/aegis-cafeteria-services/cafeteria.services.db || exit 1
```

Before activating, verify configuration is carried forward, all data/log/diagnostic/identity paths are absolute and writable under the actual unit hardening, and the database environment setting still names the confirmed existing file. Do not install the example production JSON over live settings.

For the repository's unit layout, an approved activation can use a reviewed systemd drop-in:

```sh
sudo systemctl edit aegis-cafeteria-services
# Enter these reviewed lines, adjusting the release path if necessary:
# [Service]
# WorkingDirectory=/opt/aegis-cafeteria-services/releases/2026-11-16-reviewed
# ExecStart=
# ExecStart=/usr/bin/dotnet /opt/aegis-cafeteria-services/releases/2026-11-16-reviewed/Aegis.Cafeteria.Services.dll
sudo systemctl daemon-reload
sudo systemctl restart aegis-cafeteria-services
sudo systemctl status aegis-cafeteria-services --no-pager
sudo journalctl -u aegis-cafeteria-services -n 100 --no-pager
curl --fail --silent --show-error http://127.0.0.1:5088/health
```

Confirm the actual health route and binding before using the final command. Preserve the original unit environment and absolute database setting. Do not activate if deployed storage paths still depend on the old working directory; resolve those paths through an independently approved configuration change first. No migration/reset command is part of this procedure.
