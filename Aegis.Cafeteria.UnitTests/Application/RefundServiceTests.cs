namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.UnitTests.TestDoubles;

public sealed class RefundServiceTests
{
    private readonly RefundService _service = new(new AllowRefundAuthorizer());

    [Fact]
    public void FullRefund_RestoresOriginalTenderAmounts()
    {
        var order = CompletedOrder(700m, 300m);
        var item = Assert.Single(order.Items);

        var refund = _service.Refund(
            order,
            [(item.Id, 1)],
            cafeteriaAccountAmount: 700m,
            cashAmount: 300m,
            "manager",
            DateTimeOffset.UtcNow,
            "Incorrect order");

        Assert.Equal(1_000m, refund.Total);
        Assert.Equal(700m, order.CafeteriaAccountRefunded);
        Assert.Equal(300m, order.CashRefunded);
        Assert.Equal(0m, order.NetCafeteriaAccountCharge);
        Assert.Equal(0m, order.NetCashCollected);
        Assert.Equal(RefundStatus.Full, order.RefundStatus);
    }

    [Fact]
    public void PartialRefund_CanReturnSpecificItemUsingExplicitTenderAllocation()
    {
        var order = CompletedTwoItemOrder();
        var lunch = order.Items.Single(x => x.ProductName == "Lunch");

        _service.Refund(
            order,
            [(lunch.Id, 1)],
            cafeteriaAccountAmount: 500m,
            cashAmount: 150m,
            "manager",
            DateTimeOffset.UtcNow,
            "Lunch returned");

        Assert.Equal(650m, order.RefundedAmount);
        Assert.Equal(RefundStatus.Partial, order.RefundStatus);
        Assert.Equal(500m, order.CafeteriaAccountRefunded);
        Assert.Equal(150m, order.CashRefunded);
    }

    [Fact]
    public void RepeatedRefunds_CannotExceedOriginalItemQuantity()
    {
        var order = CompletedOrder(650m, 0m, quantity: 1, unitPrice: 650m);
        var item = Assert.Single(order.Items);

        _service.Refund(order, [(item.Id, 1)], 650m, 0m, "manager", DateTimeOffset.UtcNow, "First");

        Assert.Throws<InvalidOperationException>(() =>
            _service.Refund(order, [(item.Id, 1)], 650m, 0m, "manager", DateTimeOffset.UtcNow, "Second"));
    }

    [Fact]
    public void Refund_CannotReturnMoreCashThanWasOriginallyPaidInCash()
    {
        var order = CompletedOrder(700m, 300m);
        var item = Assert.Single(order.Items);

        Assert.Throws<InvalidOperationException>(() =>
            _service.Refund(order, [(item.Id, 1)], 600m, 400m, "manager", DateTimeOffset.UtcNow, "Wrong tender"));
    }

    private static Order CompletedOrder(
        decimal account,
        decimal cash,
        int quantity = 1,
        decimal? unitPrice = null)
    {
        var total = account + cash;
        var order = new Order(Guid.NewGuid(), 10, DateTimeOffset.UtcNow);
        order.AddItem(new OrderItem(1, "Purchase", unitPrice ?? (total / quantity), quantity));
        order.Submit(DateTimeOffset.UtcNow);
        var payments = new List<OrderPayment>();
        if (account > 0)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, account, DateTimeOffset.UtcNow));
        if (cash > 0)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, cash, DateTimeOffset.UtcNow));
        order.Complete(payments, DateTimeOffset.UtcNow);
        return order;
    }

    private static Order CompletedTwoItemOrder()
    {
        var order = new Order(Guid.NewGuid(), 10, DateTimeOffset.UtcNow);
        order.AddItem(new OrderItem(1, "Lunch", 650m, 1));
        order.AddItem(new OrderItem(2, "Juice", 350m, 1));
        order.Submit(DateTimeOffset.UtcNow);
        order.Complete(
            [
                new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, DateTimeOffset.UtcNow),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, DateTimeOffset.UtcNow)
            ],
            DateTimeOffset.UtcNow);
        return order;
    }
}
