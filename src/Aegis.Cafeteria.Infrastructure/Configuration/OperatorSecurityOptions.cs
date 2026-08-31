namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class OperatorSecurityOptions
{
    public bool Enabled { get; init; } = true;
    public string Domain { get; init; } = "fftpj.local";
    public string SearchBase { get; init; } = "DC=fftpj,DC=local";
    public int Port { get; init; } = 636;
    public int TimeoutSeconds { get; init; } = 5;
    public IReadOnlyList<string> Servers { get; init; } = ["jmad01.fftpj.local", "jmad02.fftpj.local"];
    public IReadOnlyList<string> AdministrativeGroups { get; init; } = ["Cafeteria Managers", "Cafeteria Administrators", "Domain Admins"];
    public int ManagerAuthorizationMinutes { get; init; } = 2;
}
