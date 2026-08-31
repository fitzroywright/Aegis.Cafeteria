namespace Aegis.Cafeteria.Application.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Orders;

public sealed class RefundService(IRefundAuthorizer authorizer)
{
    public OrderRefund Refund(
        Order order,
        IEnumerable<(Guid OrderItemId, int Quantity)> items,
        decimal cafeteriaAccountAmount,
        decimal cashAmount,
        string refundedBy,
        DateTimeOffset refundedAt,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(items);
        if (!authorizer.CanRefund(refundedBy))
            throw new UnauthorizedAccessException("The user is not authorized to refund orders.");
        if (order.Status != OrderStatus.Completed)
            throw new InvalidOperationException("Only completed orders can be refunded.");
        if (cafeteriaAccountAmount < 0) throw new ArgumentOutOfRangeException(nameof(cafeteriaAccountAmount));
        if (cashAmount < 0) throw new ArgumentOutOfRangeException(nameof(cashAmount));

        var requested = items.ToArray();
        if (requested.Length == 0)
            throw new ArgumentException("At least one order item must be refunded.", nameof(items));

        var refundItems = requested.Select(request =>
        {
            var orderItem = order.Items.SingleOrDefault(x => x.Id == request.OrderItemId)
                ?? throw new InvalidOperationException("A requested refund item does not exist on the order.");
            if (request.Quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(items), "Refund quantity must be greater than zero.");

            var alreadyRefunded = order.GetRefundedQuantity(orderItem.Id);
            if (alreadyRefunded + request.Quantity > orderItem.Quantity)
                throw new InvalidOperationException("Refund quantity exceeds the remaining refundable quantity.");

            return new OrderRefundItem(orderItem.Id, request.Quantity, orderItem.UnitPrice);
        }).ToArray();

        var refundTotal = refundItems.Sum(x => x.Total);
        if (cafeteriaAccountAmount + cashAmount != refundTotal)
            throw new InvalidOperationException("Refund tender allocations must equal the refunded item total.");

        var allocations = new List<RefundAllocation>();
        if (cafeteriaAccountAmount > 0)
            allocations.Add(new RefundAllocation(PaymentMethod.CafeteriaAccount, cafeteriaAccountAmount));
        if (cashAmount > 0)
            allocations.Add(new RefundAllocation(PaymentMethod.Cash, cashAmount));

        var refund = new OrderRefund(
            Guid.NewGuid(), order.Id, refundItems, allocations, refundedBy, refundedAt, reason);

        order.RecordRefund(refund);
        return refund;
    }

    public LateRefundResult RefundWithPeriodHandling(
        Order order,
        IEnumerable<(Guid OrderItemId, int Quantity)> items,
        decimal cafeteriaAccountAmount,
        decimal cashAmount,
        string refundedBy,
        DateTimeOffset refundedAt,
        string reason,
        AccountPeriod sourcePeriod,
        AccountPeriod appliedPeriod,
        AccountPeriodClose? sourcePeriodClose)
    {
        var refund = Refund(order, items, cafeteriaAccountAmount, cashAmount, refundedBy, refundedAt, reason);
        PriorPeriodRefundCredit? credit = null;

        if (sourcePeriodClose is not null &&
            (appliedPeriod.Year, appliedPeriod.Month).CompareTo((sourcePeriod.Year, sourcePeriod.Month)) > 0 &&
            cafeteriaAccountAmount > 0)
        {
            credit = new PriorPeriodRefundCredit(
                Guid.NewGuid(), order.CustomerId, order.Id, refund.Id,
                sourcePeriod.Year, sourcePeriod.Month,
                appliedPeriod.Year, appliedPeriod.Month,
                cafeteriaAccountAmount, refundedBy, refundedAt,
                $"Late refund from {sourcePeriod.Year:D4}-{sourcePeriod.Month:D2}: {reason}");
        }

        return new LateRefundResult(refund, credit);
    }

    public OrderRefund RefundAllRemaining(
        Order order,
        string refundedBy,
        DateTimeOffset refundedAt,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(order);
        if (order.Status != OrderStatus.Completed)
            throw new InvalidOperationException("Only completed orders can be refunded.");

        var remainingItems = order.Items
            .Select(item => (Item: item, Remaining: item.Quantity - order.GetRefundedQuantity(item.Id)))
            .Where(x => x.Remaining > 0)
            .ToArray();

        if (remainingItems.Length == 0)
            throw new InvalidOperationException("The order has already been fully refunded.");

        var remainingTotal = remainingItems.Sum(x => x.Item.UnitPrice * x.Remaining);
        var remainingAccount = order.CafeteriaAccountPaid - order.CafeteriaAccountRefunded;
        var remainingCash = order.CashPaid - order.CashRefunded;

        if (remainingAccount + remainingCash != remainingTotal)
            throw new InvalidOperationException("Remaining tender does not match the remaining refundable order value.");

        return Refund(order, remainingItems.Select(x => (x.Item.Id, x.Remaining)), remainingAccount, remainingCash, refundedBy, refundedAt, reason);
    }
}
