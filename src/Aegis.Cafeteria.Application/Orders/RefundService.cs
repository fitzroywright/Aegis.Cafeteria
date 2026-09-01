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

        (Guid OrderItemId, int Quantity)[] requested = items.ToArray();
        if (requested.Length == 0)
            throw new ArgumentException("At least one order item must be refunded.", nameof(items));

        OrderRefundItem[] refundItems = BuildRefundItems(order, requested);
        decimal refundTotal = refundItems.Sum(x => x.Total);
        decimal creditAmount = refundTotal - cafeteriaAccountAmount - cashAmount;

        return Refund(new RefundRequest(
            order,
            refundItems,
            cafeteriaAccountAmount,
            cashAmount,
            creditAmount,
            refundedBy,
            refundedAt,
            reason));
    }

    public OrderRefund Refund(RefundRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Order);
        ArgumentNullException.ThrowIfNull(request.Items);

        if (!authorizer.CanRefund(request.RefundedBy))
            throw new UnauthorizedAccessException("The user is not authorized to refund orders.");
        if (request.Order.Status != OrderStatus.Completed)
            throw new InvalidOperationException("Only completed orders can be refunded.");
        if (request.CafeteriaAccountAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(request.CafeteriaAccountAmount));
        if (request.CashAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(request.CashAmount));
        if (request.CreditAmount < 0m)
            throw new ArgumentOutOfRangeException(nameof(request.CreditAmount));

        OrderRefundItem[] refundItems = request.Items.ToArray();
        if (refundItems.Length == 0)
            throw new ArgumentException("At least one order item must be refunded.", nameof(request.Items));

        decimal refundTotal = refundItems.Sum(x => x.Total);
        if (request.CafeteriaAccountAmount + request.CashAmount + request.CreditAmount != refundTotal)
            throw new InvalidOperationException("Refund tender allocations must equal the refunded item total.");

        List<RefundAllocation> allocations = [];
        if (request.CafeteriaAccountAmount > 0m)
            allocations.Add(new RefundAllocation(PaymentMethod.CafeteriaAccount, request.CafeteriaAccountAmount));
        if (request.CreditAmount > 0m)
            allocations.Add(new RefundAllocation(PaymentMethod.Credit, request.CreditAmount));
        if (request.CashAmount > 0m)
            allocations.Add(new RefundAllocation(PaymentMethod.Cash, request.CashAmount));

        OrderRefund refund = new(
            Guid.NewGuid(),
            request.Order.Id,
            refundItems,
            allocations,
            request.RefundedBy,
            request.RefundedAt,
            request.Reason);

        request.Order.RecordRefund(refund);
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
        OrderRefund refund = Refund(order, items, cafeteriaAccountAmount, cashAmount, refundedBy, refundedAt, reason);
        PriorPeriodRefundCredit? credit = null;

        if (sourcePeriodClose is not null &&
            (appliedPeriod.Year, appliedPeriod.Month).CompareTo((sourcePeriod.Year, sourcePeriod.Month)) > 0 &&
            cafeteriaAccountAmount > 0m)
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

        (OrderItem Item, int Remaining)[] remainingItems = order.Items
            .Select(item => (Item: item, Remaining: item.Quantity - order.GetRefundedQuantity(item.Id)))
            .Where(x => x.Remaining > 0)
            .ToArray();

        if (remainingItems.Length == 0)
            throw new InvalidOperationException("The order has already been fully refunded.");

        decimal remainingTotal = remainingItems.Sum(x => x.Item.UnitPrice * x.Remaining);
        decimal remainingAccount = order.CafeteriaAccountPaid - order.CafeteriaAccountRefunded;
        decimal remainingCash = order.CashPaid - order.CashRefunded;
        decimal remainingCredit = order.CreditPaid - order.CreditRefunded;

        if (remainingAccount + remainingCash + remainingCredit != remainingTotal)
            throw new InvalidOperationException("Remaining tender does not match the remaining refundable order value.");

        return Refund(
            order,
            remainingItems.Select(x => (x.Item.Id, x.Remaining)),
            remainingAccount,
            remainingCash,
            refundedBy,
            refundedAt,
            reason);
    }

    private static OrderRefundItem[] BuildRefundItems(
        Order order,
        IReadOnlyCollection<(Guid OrderItemId, int Quantity)> requested)
    {
        if (order.Status != OrderStatus.Completed)
            throw new InvalidOperationException("Only completed orders can be refunded.");

        return requested.Select(request =>
        {
            OrderItem orderItem = order.Items.SingleOrDefault(x => x.Id == request.OrderItemId)
                ?? throw new InvalidOperationException("A requested refund item does not exist on the order.");
            if (request.Quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(requested), "Refund quantity must be greater than zero.");

            int alreadyRefunded = order.GetRefundedQuantity(orderItem.Id);
            if (alreadyRefunded + request.Quantity > orderItem.Quantity)
                throw new InvalidOperationException("Refund quantity exceeds the remaining refundable quantity.");

            return new OrderRefundItem(orderItem.Id, request.Quantity, orderItem.UnitPrice);
        }).ToArray();
    }
}

public sealed record RefundRequest(
    Order Order,
    IReadOnlyCollection<OrderRefundItem> Items,
    decimal CafeteriaAccountAmount,
    decimal CashAmount,
    decimal CreditAmount,
    string RefundedBy,
    DateTimeOffset RefundedAt,
    string Reason);
