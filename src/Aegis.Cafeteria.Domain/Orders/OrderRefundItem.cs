namespace Aegis.Cafeteria.Domain.Orders;

public sealed class OrderRefundItem
{
    public OrderRefundItem(Guid orderItemId, int quantity, decimal unitPrice)
    {
        if (orderItemId == Guid.Empty)
            throw new ArgumentException("Order item ID is required.", nameof(orderItemId));
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));

        OrderItemId = orderItemId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid OrderItemId { get; }
    public int Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal Total => Quantity * UnitPrice;
}
