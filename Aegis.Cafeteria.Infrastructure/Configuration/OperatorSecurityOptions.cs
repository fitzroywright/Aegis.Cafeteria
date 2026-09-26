namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class OperatorSecurityOptions
{
    public bool Enabled { get; init; } = true;
    public string Domain { get; init; } = string.Empty;
    public string SearchBase { get; init; } = string.Empty;
    public int Port { get; init; } = 636;
    public int TimeoutSeconds { get; init; } = 5;
    public IReadOnlyList<string> Servers { get; init; } = [];
    public IReadOnlyList<string> AdministrativeGroups { get; init; } = ["Cafeteria Managers", "Cafeteria Administrators", "Domain Admins"];
    public int ManagerAuthorizationMinutes { get; init; } = 2;
}
