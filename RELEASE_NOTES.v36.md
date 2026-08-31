# Aegis.Cafeteria v36 - Operator Security and Transaction Control

## Implemented

- Added production operator sign-in for the Windows POS using the established Common.Security authentication contract.
- Authentication uses Common.Security ActiveDirectory over LDAPS; Aegis.Cafeteria does not duplicate LDAP credential validation.
- Added configurable FFTPJ AD settings (domain, search base, JMAD01/JMAD02, port 636, timeout and application administrative groups).
- Added an authenticated operator session. Every authenticated user is a cashier; Common.Security `IsAdministrative` grants cafeteria-manager privilege.
- Added separate manager credential challenge for privileged refund and void actions; the cashier remains signed in after supervisor approval.
- Added manager authorization expiry (default two minutes) and service-level `IRefundAuthorizer` / `IOrderVoidAuthorizer` implementations.
- Completed orders now persist `CashierUserId` and `TerminalId` as immutable transaction audit metadata.
- Added full remaining-order refund UI. Refunds preserve original cafeteria-account/cash tender allocation and publish the existing external FFP refund projection for employee transactions.
- Added recent-sale selection for Cash Customer refunds.
- Added manager-authorized current-sale void UI. A void creates and persists a real submitted-then-voided order rather than deleting the cart with no audit trail.
- Added local order query support for loading a transaction and recent completed orders.

## Deployment requirement

`Common.Security.dll` must be deployed beside the Windows POS executable, or `Security:CommonSecurityAssemblyPath` must point to it. The adapter accepts the stable `ActiveDirectoryAuthenticationOptions` type and retains compatibility with the earlier `ActiveDirectoryOptions` name.

Manager groups are application configuration. Defaults in this package are `Cafeteria Managers`, `Cafeteria Administrators`, and `Domain Admins`. Common.Security administrative membership is direct-group membership, so create/use the intended AD groups accordingly.

## Verification status

The generation environment does not contain the .NET SDK, so a full `dotnet restore`, `dotnet build -c Release`, and `dotnet test -c Release` remains required on the Windows development machine before this package is treated as a release candidate.
