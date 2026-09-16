namespace Aegis.Cafeteria.Management;

internal sealed record ManagementSession(
    string ServiceToken,
    string UserName,
    string DisplayName,
    DateTimeOffset ExpiresAt);
