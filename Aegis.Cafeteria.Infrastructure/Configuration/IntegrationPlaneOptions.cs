namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class IntegrationPlaneOptions
{
    public bool Enabled { get; init; } = true;
    public string BaseUrl { get; init; } = string.Empty;
    public int TimeoutSeconds { get; init; } = 30;
}
