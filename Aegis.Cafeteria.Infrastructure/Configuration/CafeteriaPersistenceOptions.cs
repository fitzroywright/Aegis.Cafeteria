namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaPersistenceOptions
{
    public string LocalSqliteConnectionString { get; init; } = "Data Source=cafeteria.local.db;Cache=Shared";
    public string TerminalId { get; init; } = Environment.MachineName;
}
