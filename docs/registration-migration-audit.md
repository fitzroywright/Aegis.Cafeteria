# Cafeteria registration migration audit

Branch: `common-registration-migration`

## Scope

This audit covers the Aegis.Cafeteria.Services registration path only. It does not change Aegis.Operations, Aegis.Configuration, or Aegis.Diagnostics product behavior.

## Registration boundary

- Cafeteria Services registers only with Aegis.Configuration.
- Registration transport is provided by the standalone `Common.Registration` library.
- Cafeteria does not register with Aegis.Diagnostics.
- Aegis.Diagnostics remains a diagnostics/observability concern, not an application-registration target.
- Retry occurs only while registration is unsuccessful; a real successful registration ends the startup retry cycle.

## Configuration ownership rule

Aegis.Configuration describes and monitors configuration requirements. It does not own application configuration values.

Cafeteria may publish evidence about a requirement, including:

- requirement identifier and configuration key
- whether the requirement is required or conditional
- whether a value is configured
- whether code supplies a default
- configuration state such as Configured, Default, or Unresolved
- the type/source category used to resolve a requirement
- secret verification state without exposing a secret

Cafeteria must not publish the actual contents of:

- environment-variable values
- appsettings values
- resolved configuration values
- code defaults
- secret values
- connection strings
- display values derived from the above

The Cafeteria publisher no longer emits `defaultValue` or `safeDisplayValue`. Common.Registration recursively strips value-bearing fields again at the transport boundary and normalizes requirement-kind wire values for Aegis.Configuration.

## Failure semantics

A registration attempt is successful only when Aegis.Configuration accepts it with a successful HTTP response.

The following are registration failures and must not produce a success log:

- missing registration credential
- malformed or unreadable contract
- non-success HTTP response
- timeout
- network/transport failure

Registration failure does not stop Cafeteria Services. The service remains operational and retries publication.

## Secret handling

Secret requirements may publish existence/verification state only. Secret material is never included in the registration payload. Common.Secrets/OpenBao remains responsible for secret retrieval.

## Validation evidence

The permanent `fitzroywright/Common.Registration` repository has executable tests covering recursive metadata-only sanitization, source-contract non-mutation, requirement-kind wire normalization, sanitized transmission, HTTP rejection, timeout, and transport failure. Its current CI is green.

Cafeteria workflows now consume the standalone Common.Registration repository directly. Cafeteria's current GitHub Actions jobs are not executable validation evidence because the jobs are failing before any workflow step runs and no runner is assigned. Therefore this branch does not claim a successful Cafeteria compile/test run yet.

## Repository layout

Common.Registration is a permanent Common.* sibling library at `Common/Common.Registration`. Cafeteria's solution and CI use that location directly; no workflow should source registration code from Common.Diagnostics.