namespace Aegis.Cafeteria.UnitTests.Orders;

using Aegis.Cafeteria.Domain.Orders;

public sealed class OrderTests
{
    [Fact]
    public void NewOrder_IsDraftWithNoItemsAndZeroTotal()
    {
        var order = new Order(Guid.NewGuid(), 10, DateTimeOffset.UtcNow);

        Assert.Equal(OrderStatus.Draft, order.Status);
        Assert.Empty(order.Items);
        Assert.Equal(0m, order.Total);
    }

    [Fact]
    public void Total_IsCalculatedFromItems()
    {
        var order = CreateDraftOrder();
        order.AddItem(new OrderItem(1, "Lunch", 650m, 1));
        order.AddItem(new OrderItem(2, "Juice", 150m, 2));

        Assert.Equal(950m, order.Total);
    }

    [Fact]
    public void Submit_LocksOrderItems()
    {
        var order = CreateDraftOrder();
        order.AddItem(new OrderItem(1, "Lunch", 650m, 1));
        order.Submit(DateTimeOffset.UtcNow);

        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Throws<InvalidOperationException>(() =>
            order.AddItem(new OrderItem(2, "Juice", 150m, 1)));
    }

    [Fact]
    public void Submit_EmptyOrder_Throws()
    {
        var order = CreateDraftOrder();
        Assert.Throws<InvalidOperationException>(() => order.Submit(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Complete_RequiresPaymentsToEqualTotal()
    {
        var order = SubmittedOrder(1_000m);

        Assert.Throws<InvalidOperationException>(() =>
            order.Complete(
                [new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 900m, DateTimeOffset.UtcNow)],
                DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Complete_SupportsSplitTender()
    {
        var order = SubmittedOrder(1_000m);
        var now = DateTimeOffset.UtcNow;

        order.Complete(
            [
                new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, now),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, now)
            ],
            now);

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(700m, order.CafeteriaAccountPaid);
        Assert.Equal(300m, order.CashPaid);
        Assert.Equal(1_000m, order.AmountPaid);
    }

    [Fact]
    public void Void_RequiresReasonAndKeepsAuditInformation()
    {
        var order = SubmittedOrder(650m);
        var when = DateTimeOffset.UtcNow;

        order.Void("operator", when, "Customer changed mind");

        Assert.Equal(OrderStatus.Voided, order.Status);
        Assert.Equal("operator", order.VoidedBy);
        Assert.Equal(when, order.VoidedAt);
        Assert.Equal("Customer changed mind", order.VoidReason);
    }

    [Fact]
    public void CompletedOrder_CannotBeVoided()
    {
        var order = CompletedOrder(650m, 650m, 0m);

        Assert.Throws<InvalidOperationException>(() =>
            order.Void("operator", DateTimeOffset.UtcNow, "Too late"));
    }

    private static Order CreateDraftOrder()
        => new(Guid.NewGuid(), 10, DateTimeOffset.UtcNow);

    private static Order SubmittedOrder(decimal total)
    {
        var order = CreateDraftOrder();
        order.AddItem(new OrderItem(1, "Purchase", total, 1));
        order.Submit(DateTimeOffset.UtcNow);
        return order;
    }

    private static Order CompletedOrder(decimal total, decimal account, decimal cash)
    {
        var order = SubmittedOrder(total);
        var payments = new List<OrderPayment>();
        if (account > 0)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, account, DateTimeOffset.UtcNow));
        if (cash > 0)
            payments.Add(new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, cash, DateTimeOffset.UtcNow));
        order.Complete(payments, DateTimeOffset.UtcNow);
        return order;
    }
}
