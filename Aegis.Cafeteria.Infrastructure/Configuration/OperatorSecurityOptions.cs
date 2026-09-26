namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class OperatorSecurityOptions
{
    public bool Enabled { get; init; } = true;
    public IReadOnlyList<string> AdministrativeGroups { get; init; } =
    [
        "Cafeteria Managers",
        "Cafeteria Administrators",
        "Domain Admins"
    ];
    public int ManagerAuthorizationMinutes { get; init; } = 2;
}
