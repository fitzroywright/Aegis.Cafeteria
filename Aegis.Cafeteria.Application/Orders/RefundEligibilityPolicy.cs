namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Domain.Orders;

public sealed record RefundEligibility
(
      bool Allowed
    , string? Reason
);

public static class RefundEligibilityPolicy
{
    public static RefundEligibility Evaluate(Order? order)
    {
        if (order is null)
        {
            return new RefundEligibility(false, "The selected order could not be found.");
        }

        if (order.Status != OrderStatus.Completed)
        {
            return new RefundEligibility(false, "Only completed orders can be refunded.");
        }

        if (order.RefundedAmount >= order.Total)
        {
            return new RefundEligibility(false, "The order has already been fully refunded.");
        }

        if (!order.Items.Any(item => item.Quantity - order.GetRefundedQuantity(item.Id) > 0))
        {
            return new RefundEligibility(false, "The order has no remaining refundable item quantity.");
        }

        return new RefundEligibility(true, null);
    }
}
