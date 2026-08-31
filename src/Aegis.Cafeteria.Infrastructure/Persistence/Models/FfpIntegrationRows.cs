namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public enum FfpOutboxState { Pending = 0, Processing = 1, Succeeded = 2, Failed = 3, Poisoned = 4 }

public sealed class FfpLedgerOutboxRow
{
    public Guid OperationId { get; set; }
    public int EmployeeId { get; set; }
    public int Kind { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal ScanValue { get; set; }
    public decimal CreditLimit { get; set; }
    public decimal AvailableBalance { get; set; }
    public string ScanCode { get; set; } = "AEGIS";
    public string UserName { get; set; } = "SYSTEM";
    public string? IdName { get; set; }
    public FfpOutboxState State { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}
