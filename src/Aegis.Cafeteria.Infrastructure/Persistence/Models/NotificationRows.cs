namespace Aegis.Cafeteria.Infrastructure.Persistence.Models;

public sealed class WeeklySummaryDispatchRow
{
    public int CustomerId { get; set; }
    public DateOnly WeekStart { get; set; }
    public Guid MessageId { get; set; }
    public DateTimeOffset SentAt { get; set; }
}
