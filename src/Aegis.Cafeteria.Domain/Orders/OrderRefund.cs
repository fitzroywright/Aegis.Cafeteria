namespace Aegis.Cafeteria.Domain.Orders;

public sealed class OrderRefund
{
    private readonly IReadOnlyCollection<OrderRefundItem> items;
    private readonly IReadOnlyCollection<RefundAllocation> allocations;

    public OrderRefund(
        Guid id,
        Guid orderId,
        IEnumerable<OrderRefundItem> items,
        IEnumerable<RefundAllocation> allocations,
        string refundedBy,
        DateTimeOffset refundedAt,
        string reason)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Refund ID is required.", nameof(id));
        if (orderId == Guid.Empty)
            throw new ArgumentException("Order ID is required.", nameof(orderId));
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(allocations);
        if (string.IsNullOrWhiteSpace(refundedBy))
            throw new ArgumentException("Refunding user is required.", nameof(refundedBy));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A refund reason is required.", nameof(reason));

        OrderRefundItem[] itemArray = items.ToArray();
        RefundAllocation[] allocationArray = allocations.ToArray();
        if (itemArray.Length == 0)
            throw new ArgumentException("A refund must contain at least one item.", nameof(items));
        if (allocationArray.Length == 0)
            throw new ArgumentException("A refund must contain at least one tender allocation.", nameof(allocations));

        decimal itemTotal = itemArray.Sum(x => x.Total);
        decimal allocationTotal = allocationArray.Sum(x => x.Amount);
        if (itemTotal != allocationTotal)
            throw new ArgumentException("Refund allocations must equal the refunded item total.", nameof(allocations));

        Id = id;
        OrderId = orderId;
        this.items = Array.AsReadOnly(itemArray);
        this.allocations = Array.AsReadOnly(allocationArray);
        RefundedBy = refundedBy.Trim();
        RefundedAt = refundedAt;
        Reason = reason.Trim();
    }

    public Guid Id { get; }
    public Guid OrderId { get; }
    public IReadOnlyCollection<OrderRefundItem> Items => this.items;
    public IReadOnlyCollection<RefundAllocation> Allocations => this.allocations;
    public string RefundedBy { get; }
    public DateTimeOffset RefundedAt { get; }
    public string Reason { get; }
    public decimal Total => Items.Sum(x => x.Total);
    public decimal CafeteriaAccountAmount => Allocations.Where(x => x.Method == PaymentMethod.CafeteriaAccount).Sum(x => x.Amount);
    public decimal CashAmount => Allocations.Where(x => x.Method == PaymentMethod.Cash).Sum(x => x.Amount);
    public decimal CreditAmount => Allocations.Where(x => x.Method == PaymentMethod.Credit).Sum(x => x.Amount);
}
