namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaLoggingOptions
{
    public string ApplicationName { get; init; } = "Aegis.Cafeteria";
    public string GraylogHost { get; init; } = "10.10.0.3";
    public int GraylogPort { get; init; } = 1699;
    public bool GraylogEnabled { get; init; } = true;
    public string LocalLogPath { get; init; } = "logs/aegis-cafeteria-.log";
    public int RetainedFileCountLimit { get; init; } = 31;
}
