namespace Aegis.Cafeteria.Plugin.Integration;

public sealed class IntegrationPluginOptions
{
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
    public int EmployeeRefreshIntervalMinutes { get; init; } = 5;
    public string EmployeeCachePath { get; init; } = "data/integration-employees.json";
}
