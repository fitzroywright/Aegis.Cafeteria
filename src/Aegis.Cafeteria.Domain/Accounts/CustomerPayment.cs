namespace Aegis.Cafeteria.Domain.Accounts;

public sealed class CustomerPayment
{
    public CustomerPayment(
        Guid id,
        int customerId,
        decimal amount,
        DateTimeOffset receivedAt,
        string? reference = null,
        string? paymentMethod = null,
        string? receivedByUserId = null,
        decimal? outstandingBalanceBefore = null,
        decimal? outstandingBalanceAfter = null)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Payment ID is required.", nameof(id));
        if (customerId <= 0)
            throw new ArgumentOutOfRangeException(nameof(customerId));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        if (outstandingBalanceBefore < 0m)
            throw new ArgumentOutOfRangeException(nameof(outstandingBalanceBefore));
        if (outstandingBalanceAfter < 0m)
            throw new ArgumentOutOfRangeException(nameof(outstandingBalanceAfter));
        if (outstandingBalanceBefore.HasValue && outstandingBalanceAfter.HasValue && outstandingBalanceAfter.Value > outstandingBalanceBefore.Value)
            throw new ArgumentException("Outstanding balance after payment cannot exceed the balance before payment.", nameof(outstandingBalanceAfter));

        Id = id;
        CustomerId = customerId;
        Amount = amount;
        ReceivedAt = receivedAt;
        Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim();
        PaymentMethod = string.IsNullOrWhiteSpace(paymentMethod) ? "Unspecified" : paymentMethod.Trim();
        ReceivedByUserId = string.IsNullOrWhiteSpace(receivedByUserId) ? "SYSTEM" : receivedByUserId.Trim();
        OutstandingBalanceBefore = outstandingBalanceBefore;
        OutstandingBalanceAfter = outstandingBalanceAfter;
    }

    public Guid Id { get; }
    public int CustomerId { get; }
    public decimal Amount { get; }
    public DateTimeOffset ReceivedAt { get; }
    public string? Reference { get; }
    public string PaymentMethod { get; }
    public string ReceivedByUserId { get; }
    public decimal? OutstandingBalanceBefore { get; }
    public decimal? OutstandingBalanceAfter { get; }
}
