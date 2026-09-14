namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaPersistenceOptions
{
    public string LocalSqliteConnectionString { get; init; } = "Data Source=cafeteria.local.db;Cache=Shared";
    public string TerminalId { get; init; } = Environment.MachineName;
    public int FfpPublicationBatchSize { get; init; } = 100;
    public int FfpPoisonMessageAttemptLimit { get; init; } = 25;
}
