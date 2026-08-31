namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class AdditionalCreditApproval
{
    public AdditionalCreditApproval(
        Guid id,
        int customerId,
        decimal amount,
        string approvedBy,
        DateTimeOffset approvedAt,
        string? reason = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Approval ID is required.", nameof(id));
        if (customerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(customerId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (string.IsNullOrWhiteSpace(approvedBy))
            throw new ArgumentException("Approver is required.", nameof(approvedBy));

        Id = id;
        CustomerId = customerId;
        Amount = amount;
        ApprovedBy = approvedBy.Trim();
        ApprovedAt = approvedAt;
        Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public decimal Amount { get; }
    public string ApprovedBy { get; }
    public DateTimeOffset ApprovedAt { get; }
    public string? Reason { get; }
}
