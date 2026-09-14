namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public sealed class FfpImportedTransactionRow
{
    public long SourceId { get; set; }
    public int CustomerId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string IdName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal ScanValue { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string ScanCode { get; set; } = string.Empty;
    public int ScanType { get; set; }
    public DateTimeOffset ImportedAt { get; set; }
}
