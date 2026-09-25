namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaServicesOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "http://localhost:5088";
    public int PollSeconds { get; init; } = 15;
    public int RequestTimeoutSeconds { get; init; } = 10;
    public int StartupRefreshTimeoutSeconds { get; init; } = 12;
    public int ReferenceRefreshIntervalMinutes { get; init; } = 10;
    public string? ApiKey { get; init; }
    public bool RealtimeEnabled { get; init; } = true;
    public string HubPath { get; init; } = "/hubs/cafeteria";
    public int RealtimeReconnectSeconds { get; init; } = 5;
}
