namespace Aegis.Cafeteria.Infrastructure.Configuration;

public sealed class FfpIntegrationOptions
{
    public bool Enabled { get; init; } = true;
    public string ConnectionString { get; init; } = string.Empty;
    public int RefreshIntervalMinutes { get; init; } = 10;
    public string CreditLimitLunchItemCode { get; init; } = "FFP00";
    public string SubsidyLunchItemCode { get; init; } = "FFP00";

    public int MonthlyAllocationScanType { get; init; } = 3;
    public int PurchaseScanType { get; init; } = 9;
    public int RefundScanType { get; init; } = 9;
    public int AdjustmentScanType { get; init; } = 9;
}
