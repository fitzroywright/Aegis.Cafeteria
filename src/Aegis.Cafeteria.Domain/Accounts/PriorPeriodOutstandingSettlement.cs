namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class PriorPeriodOutstandingSettlement
{
    public PriorPeriodOutstandingSettlement(
        Guid id,
        int customerId,
        int sourceYear,
        int sourceMonth,
        int settlementYear,
        int settlementMonth,
        decimal amount,
        string approvedBy,
        DateTimeOffset approvedAt,
        string reason)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Settlement ID is required.", nameof(id));
        if (customerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(customerId));
        if (sourceYear < 1)
            throw new ArgumentOutOfRangeException(nameof(sourceYear));
        if (sourceMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(sourceMonth));
        if (settlementYear < 1)
            throw new ArgumentOutOfRangeException(nameof(settlementYear));
        if (settlementMonth is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(settlementMonth));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("Approver is required.", nameof(approvedBy));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A reason is required for a prior-period settlement.", nameof(reason));

        var source = new DateOnly(sourceYear, sourceMonth, 1);
        var settlement = new DateOnly(settlementYear, settlementMonth, 1);
        if (source >= settlement)
            throw new ArgumentException("The source period must be earlier than the settlement period.");

        Id = id;
        CustomerId = customerId;
        SourceYear = sourceYear;
        SourceMonth = sourceMonth;
        SettlementYear = settlementYear;
        SettlementMonth = settlementMonth;
        Amount = amount;
        ApprovedBy = approvedBy.Trim();
        ApprovedAt = approvedAt;
        Reason = reason.Trim();
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public int SourceYear { get; }
    public int SourceMonth { get; }
    public int SettlementYear { get; }
    public int SettlementMonth { get; }
    public decimal Amount { get; }
    public string ApprovedBy { get; }
    public DateTimeOffset ApprovedAt { get; }
    public string Reason { get; }
}
