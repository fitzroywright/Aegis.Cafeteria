namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaHealthOptions
{
    public bool Enabled { get; init; } = true;
    public int IntervalSeconds { get; init; } = 60;
    public string HeartbeatPath { get; init; } = @"C:\ProgramData\Aegis.Cafeteria\Health\heartbeat.json";
    public int GraylogHeartbeatEveryMinutes { get; init; } = 5;
    public int EmployeeCacheWarningMinutes { get; init; } = 120;
    public int EmployeeCacheCriticalMinutes { get; init; } = 480;
}
