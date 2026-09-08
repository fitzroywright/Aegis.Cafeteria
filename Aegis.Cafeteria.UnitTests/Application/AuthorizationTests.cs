namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.UnitTests.TestDoubles;

public sealed class AuthorizationTests
{
    [Fact]
    public void Void_WhenUnauthorized_Throws()
    {
        var service = new OrderLifecycleService(new PurchaseAuthorizationService(), new DenyVoidAuthorizer());
        var order = service.Create(1, DateTimeOffset.UtcNow);
        order.AddItem(new OrderItem(1, "Lunch", 650m, 1));
        Assert.Throws<UnauthorizedAccessException>(() => service.Void(order, "user", DateTimeOffset.UtcNow, "Mistake"));
    }

    [Fact]
    public void Refund_WhenUnauthorized_Throws()
    {
        var service = new RefundService(new DenyRefundAuthorizer());
        var when = DateTimeOffset.UtcNow;
        var order = new Order(Guid.NewGuid(), 1, when);
        var item = new OrderItem(1, "Lunch", 650m, 1);
        order.AddItem(item);
        order.Submit(when);
        order.Complete([new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 650m, when)], when);
        Assert.Throws<UnauthorizedAccessException>(() => service.Refund(order, [(item.Id, 1)], 650m, 0m, "user", when, "Mistake"));
    }
}
