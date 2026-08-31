# Common.Security integration — v48

`Common.Security` remains a compile-time project reference from `Aegis.Cafeteria.Infrastructure` (`..\Common.Security\Common.Security.csproj`). No DLL-path/reflection loader is used.

The POS authenticates operators once through `IAuthenticator.Authenticate(loginName,password)`. Any authenticated operator may perform normal cashier sales. `AuthenticationResult.IsAdministrative` is used by Aegis manager authorizers for privileged refund/void operations. A manager can approve an operation with a separate credential challenge without replacing the cashier session.

Production AD settings are application configuration: domain `fftpj.local`, LDAPS 636, search base `DC=fftpj,DC=local`, and configured DC FQDNs. The workstation must trust the FFTPJ issuing CA. Never bypass certificate validation or store an AD password.
