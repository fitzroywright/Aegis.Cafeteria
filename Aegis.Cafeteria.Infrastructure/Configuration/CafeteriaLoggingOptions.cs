namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaLoggingOptions
{
    public string ApplicationName { get; init; } = "Aegis.Cafeteria";
    public string GraylogHost { get; init; } = string.Empty;
    public int GraylogPort { get; init; } = 1699;
    public bool GraylogEnabled { get; init; }
    public string LocalLogPath { get; init; } = "logs/aegis-cafeteria-.log";
    public int RetainedFileCountLimit { get; init; } = 31;
}
