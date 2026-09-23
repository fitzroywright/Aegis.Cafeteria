namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaUiOptions
{
    /// <summary>
    /// Number of seconds without keyboard or mouse activity before an identified customer
    /// and any in-progress cart are cleared from the POS. Set to 0 to disable.
    /// </summary>
    public int CustomerInactivityTimeoutSeconds { get; init; } = 60;

    /// <summary>Number of today's completed transactions shown on the POS dashboard. Set to 0 to hide.</summary>
    public int RecentTransactionCount { get; init; } = 10;}
