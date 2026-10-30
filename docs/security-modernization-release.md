# Security modernization candidate — deployment gate closed

This is a coordinated source candidate, not an approved production rollout. `security-baseline.json` pins every shared library; no application may substitute a floating Common.Security checkout or overwrite shared running binaries. Build in an isolated output directory and deploy a complete application bundle independently.

## Build and evidence

Run `python3 ../Common/Common.Security/scripts/verify-source-baseline.py security-baseline.json --common-root ../Common` before building. Run the existing application tests and the coordinated security workflow. Failed, skipped, missing or empty results block release. The workflow uses disposable PostgreSQL 16 and cannot replace live provider or operational acceptance.

## Data and identity preparation

1. Capture the currently deployed application package, protected configuration, database backup, key rings, plugin versions and credential-store references. Verify a restore into an isolated environment before any production write.
2. Review an owner-only approved account manifest. Preserve each existing account ID and login alias. Resolve immutable AD domain/user GUIDs or Entra tenant/object IDs through an authorized directory administrator. Email and usernames do not authorize bindings. No production identity import has been executed by this change.
3. Rehearse the additive common schema and approved imports on a representative restored database. Verify account counts, existing business references, role assignments and security audit. New staff/operators must remain Pending Access until explicitly approved.
4. Keep at least two approved administrators. Test login, disabling, linking/unlinking, role removal and session revocation before enabling broader access. All pre-cutover human cookies require a fresh login to acquire a verified account/version binding.

## Rollout and recovery

Stage the complete candidate alongside the last known-good package. Keep each application's database and deployment independent. Run its acceptance matrix, verify protected configuration and backups, then cut over only that application. No deployment is authorized by a successful shared-library build alone.

On a stop condition, stop the affected rollout and return traffic to the previous package. Preserve new business records and the additive security tables/audit. Do not restore a stale pre-cutover database after new writes; reconcile identity changes forward or recover current business writes into a restored copy before an explicitly approved restore. Do not drop old identity history or business tables. Re-run smoke and security checks after recovery.

## Required operational acceptance

The POS continues local-first AD/LDAPS operator authentication; an unavailable Cafeteria Services session does not create a cached-password fallback. Employees, stipend accounts, operator accounts and roles remain distinct. The existing employee and financial schemas/calculations are unchanged. Operator and manager sessions use live account/version validation and bounded shared session lifetimes.

Review approved immutable operator bindings, preserving existing role aliases. Apply the approved operator manifest independently to each relevant protected local/service identity database; verify approvals and application roles before cutover. Provisioning does not grant Operator or Manager automatically. Services use the same shared operator authenticator, scoped opaque bearer sessions and shared rate-limited credential routes. Manager-only `/api/management/security/accounts` manages lifecycle, while its role endpoint changes the existing CafeteriaSecurityUserRoles table and revokes sessions transactionally. It does not create employee or financial records.

Mandatory live gates: AD/LDAPS/DC connectivity; approved operator and manager sign-in on the POS and Services; card scanning; local/offline behavior; transaction persistence and no-negative-balance rules; allocation/recalculation/month open/close/working-day proration; FFP database integration; reporting/payroll; historical employee and balance preservation. Test the Windows POS package and existing operational deployment behavior in the actual supported environment before release.

Windows management Negotiate also enters the shared verified-identity lifecycle. Preapprove the `windows-negotiate` domain/user SID binding for the existing operator account through a protected manifest; never join it by login alias. LDAP and SID proofs can bind to the same approved operator through explicit identity enrollment. The Windows path requires a real domain WindowsIdentity; password-based AD/LDAPS remains the cross-platform operator path. An empty configured group list cannot grant management or diagnostic access. Validate the actual Windows management client after this approval migration.
