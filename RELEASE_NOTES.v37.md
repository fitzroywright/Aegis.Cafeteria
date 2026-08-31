# Aegis.Cafeteria v37

## Common.Security compile-time integration

- Added the supplied `Common.Security` production project at `src/Common.Security`.
- Added a normal `ProjectReference` from `Aegis.Cafeteria.Infrastructure` to `Common.Security`.
- Added `Common.Security` to `Aegis.Cafeteria.slnx`.
- Replaced the v36 reflection/runtime-DLL loader with direct references to:
  - `Common.Security.Abstractions.IAuthenticator`
  - `Common.Security.Authenticators.ActiveDirectory`
  - `Common.Security.Options.ActiveDirectoryAuthenticationOptions`
  - `Common.Security.Models.AuthenticationResult`
- Removed `Security:CommonSecurityAssemblyPath` from the Windows POS configuration.
- Removed `CommonSecurityAssemblyPath` from `OperatorSecurityOptions`.
- Common.Security and its dependencies are now built/copied transitively by MSBuild when the POS is built.

## Security behavior retained

- Operator login still requires Common.Security authentication.
- Authentication fails closed if no usable `UserInfo` is returned.
- `AuthenticationResult.IsAdministrative` continues to drive Cafeteria manager privileges.
- Cashier and terminal audit fields from v36 are unchanged.
- Manager-authorized refund and void functionality from v36 is unchanged.

## Release verification

The archive was structurally checked in the generation environment. The .NET SDK is not installed in that environment, so the production release gate remains:

```powershell
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
```
