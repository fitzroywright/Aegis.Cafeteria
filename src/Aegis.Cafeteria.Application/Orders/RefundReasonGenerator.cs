namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Domain.Orders;

public static class RefundReasonGenerator
{
    private const int MaximumListedItems = 5;

    public static string Generate
    (
          Order order
        , IReadOnlyCollection<(Guid OrderItemId, int Quantity)> selectedItems
    )
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(selectedItems);

        List<string> itemDescriptions = [];

        foreach ((Guid orderItemId, int quantity) in selectedItems.Where(item => item.Quantity > 0).Take(MaximumListedItems))
        {
            OrderItem? orderItem = order.Items.SingleOrDefault(item => item.Id == orderItemId);
            if (orderItem is not null)
            {
                itemDescriptions.Add($"{quantity} x {orderItem.ProductName}");
            }
        }

        int additionalItemCount = Math.Max(0, selectedItems.Count(item => item.Quantity > 0) - itemDescriptions.Count);
        string itemText = itemDescriptions.Count == 0
            ? "selected item(s)"
            : string.Join(", ", itemDescriptions);

        if (additionalItemCount > 0)
        {
            itemText += $", and {additionalItemCount} more item(s)";
        }

        string saleText = order.CompletedAt.HasValue
            ? order.CompletedAt.Value.ToString("yyyy-MM-dd HH:mm")
            : order.CreatedAt.ToString("yyyy-MM-dd HH:mm");

        return $"Refund: {itemText}; original sale {saleText}.";
    }
}
