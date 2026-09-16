namespace Aegis.Cafeteria.Management;

internal sealed record ManagementAuthenticationResponse(
    string Token,
    string UserName,
    string DisplayName,
    DateTimeOffset ExpiresAt,
    string AuthenticationSource);
