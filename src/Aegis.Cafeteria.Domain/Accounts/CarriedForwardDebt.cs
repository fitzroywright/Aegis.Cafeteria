namespace Aegis.Cafeteria.Domain.Accounts;

public sealed record CarriedForwardDebt
{
    public CarriedForwardDebt(Guid id, int customerId, int sourceYear, int sourceMonth, int appliedYear, int appliedMonth, decimal amount, DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id is required.", nameof(id));
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (sourceMonth is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(sourceMonth));
        if (appliedMonth is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(appliedMonth));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        Id = id;
        CustomerId = customerId;
        SourceYear = sourceYear;
        SourceMonth = sourceMonth;
        AppliedYear = appliedYear;
        AppliedMonth = appliedMonth;
        Amount = amount;
        RecordedAt = recordedAt;
    }
    public Guid Id { get; }
    public int CustomerId { get; }
    public int SourceYear { get; }
    public int SourceMonth { get; }
    public int AppliedYear { get; }
    public int AppliedMonth { get; }
    public decimal Amount { get; }
    public DateTimeOffset RecordedAt { get; }
}
