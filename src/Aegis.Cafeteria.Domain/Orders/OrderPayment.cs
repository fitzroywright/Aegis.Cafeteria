namespace Aegis.Cafeteria.Domain.Orders;

public sealed class OrderPayment
{
    public OrderPayment(Guid id, PaymentMethod method, decimal amount, DateTimeOffset recordedAt)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Payment ID is required.", nameof(id));
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        Id = id;
        Method = method;
        Amount = amount;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; }
    public PaymentMethod Method { get; }
    public decimal Amount { get; }
    public DateTimeOffset RecordedAt { get; }
}
