namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaMessagingOptions
{
    public bool Enabled { get; init; }
    public string Endpoint { get; init; } = string.Empty;
    public string? BearerToken { get; init; }
    public DayOfWeek WeeklySummaryDay { get; init; } = DayOfWeek.Monday;
    public int WeeklySummaryHourLocal { get; init; } = 8;
}
