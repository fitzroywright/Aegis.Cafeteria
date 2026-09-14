namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public sealed class PosTransactionOutboxRow
{
    public Guid EventId { get; set; }
    public Guid CorrelationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public int CustomerId { get; set; }
    public string Actor { get; set; } = string.Empty;
    public string? TerminalId { get; set; }
    public int AccountYear { get; set; }
    public int AccountMonth { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string PayloadJson { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? LastError { get; set; }
}
