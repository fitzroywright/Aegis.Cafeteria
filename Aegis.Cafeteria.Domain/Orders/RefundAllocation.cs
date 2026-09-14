namespace Aegis.Cafeteria.Domain.Orders;

public sealed class RefundAllocation
{
    public RefundAllocation(PaymentMethod method, decimal amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount));

        Method = method;
        Amount = amount;
    }

    public PaymentMethod Method { get; }
    public decimal Amount { get; }
}
