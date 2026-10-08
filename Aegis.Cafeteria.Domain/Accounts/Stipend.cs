namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class Stipend
{
    public Stipend(Guid id, int customerId, decimal amount, string processedByUserId, DateTimeOffset processedAt, string? reason = null)
    {
        if (id == Guid.Empty) throw new ArgumentException("Operation ID is required.", nameof(id));
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (amount <= 0m) throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(processedByUserId)) throw new ArgumentException("An authorized user is required.", nameof(processedByUserId));
        Id = id;
        CustomerId = customerId;
        Amount = amount;
        ProcessedByUserId = processedByUserId.Trim();
        ProcessedAt = processedAt;
        Reason = reason?.Trim();
    }
    public Guid Id { get; }
    public int CustomerId { get; }
    public decimal Amount { get; }
    public string ProcessedByUserId { get; }
    public DateTimeOffset ProcessedAt { get; }
    public string? Reason { get; }
}
