namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.UnitTests.TestDoubles;

public sealed class LateRefundTests
{
    [Fact]
    public void RefundAfterClosedPeriod_CreatesPriorPeriodRefundCreditForAccountTenderOnly()
    {
        var service = new RefundService(new AllowRefundAuthorizer());
        var august = new AccountPeriod(2026, 8, 22);
        var september = new AccountPeriod(2026, 9, 21);
        var when = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var order = new Order(Guid.NewGuid(), 10, when);
        var item = new OrderItem(1, "Lunch", 1000m, 1);
        order.AddItem(item);
        order.Submit(when);
        order.Complete([
            new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, when),
            new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, when)], when);
        var close = new AccountPeriodClose(Guid.NewGuid(), 10, august, 0m, "payroll", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));

        var result = service.RefundWithPeriodHandling(order, [(item.Id, 1)], 700m, 300m, "manager", new DateTimeOffset(2026, 9, 5, 10, 0, 0, TimeSpan.Zero), "Late return", august, september, close);

        Assert.NotNull(result.PriorPeriodRefundCredit);
        Assert.Equal(700m, result.PriorPeriodRefundCredit!.CafeteriaAccountAmount);
    }
}
