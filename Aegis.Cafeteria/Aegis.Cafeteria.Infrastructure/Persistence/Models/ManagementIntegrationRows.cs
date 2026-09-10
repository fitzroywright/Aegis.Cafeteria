namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public sealed class ManagementEventInboxRow
{
    public Guid OperationId { get; set; }
    public long Sequence { get; set; }
    public int Kind { get; set; }
    public int CustomerId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public string? LastError { get; set; }
}
