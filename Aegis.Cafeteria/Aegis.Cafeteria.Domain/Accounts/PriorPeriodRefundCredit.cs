namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class PriorPeriodRefundCredit
{
    public PriorPeriodRefundCredit(
        Guid id,
        int customerId,
        Guid sourceOrderId,
        Guid sourceRefundId,
        int sourceYear,
        int sourceMonth,
        int appliedYear,
        int appliedMonth,
        decimal cafeteriaAccountAmount,
        string recordedBy,
        DateTimeOffset recordedAt,
        string reason)
    {
        if (id == Guid.Empty) throw new ArgumentException("Credit ID is required.", nameof(id));
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (sourceOrderId == Guid.Empty) throw new ArgumentException("Source order ID is required.", nameof(sourceOrderId));
        if (sourceRefundId == Guid.Empty) throw new ArgumentException("Source refund ID is required.", nameof(sourceRefundId));
        if (sourceYear < 1 || appliedYear < 1) throw new ArgumentOutOfRangeException(nameof(sourceYear));
        if (sourceMonth is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(sourceMonth));
        if (appliedMonth is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(appliedMonth));
        if ((appliedYear, appliedMonth).CompareTo((sourceYear, sourceMonth)) <= 0)
            throw new ArgumentException("A prior-period refund credit must be applied after its source period.");
        if (cafeteriaAccountAmount <= 0) throw new ArgumentOutOfRangeException(nameof(cafeteriaAccountAmount));
        if (string.IsNullOrWhiteSpace(recordedBy)) throw new ArgumentException("Recording user is required.", nameof(recordedBy));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A reason is required.", nameof(reason));

        Id = id;
        CustomerId = customerId;
        SourceOrderId = sourceOrderId;
        SourceRefundId = sourceRefundId;
        SourceYear = sourceYear;
        SourceMonth = sourceMonth;
        AppliedYear = appliedYear;
        AppliedMonth = appliedMonth;
        CafeteriaAccountAmount = cafeteriaAccountAmount;
        RecordedBy = recordedBy.Trim();
        RecordedAt = recordedAt;
        Reason = reason.Trim();
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public Guid SourceOrderId { get; }
    public Guid SourceRefundId { get; }
    public int SourceYear { get; }
    public int SourceMonth { get; }
    public int AppliedYear { get; }
    public int AppliedMonth { get; }
    public decimal CafeteriaAccountAmount { get; }
    public string RecordedBy { get; }
    public DateTimeOffset RecordedAt { get; }
    public string Reason { get; }
}
