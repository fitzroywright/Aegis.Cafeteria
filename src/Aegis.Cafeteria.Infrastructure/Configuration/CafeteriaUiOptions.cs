namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class CafeteriaUiOptions
{
    /// <summary>
    /// Number of seconds without keyboard or mouse activity before an identified customer
    /// and any in-progress cart are cleared from the POS. Set to 0 to disable.
    /// </summary>
    public int CustomerInactivityTimeoutSeconds { get; init; } = 30;

    /// <summary>Number of today's completed transactions shown on the POS dashboard. Set to 0 to hide.</summary>
    public int RecentTransactionCount { get; init; } = 10;

    /// <summary>
    /// Shows the temporary FFP Data Tools card in Cafeteria Administration.
    /// Set to false after cutover stabilization. This affects UI visibility only.
    /// </summary>
    public bool ShowFfpDataTools { get; init; } = true;
}
