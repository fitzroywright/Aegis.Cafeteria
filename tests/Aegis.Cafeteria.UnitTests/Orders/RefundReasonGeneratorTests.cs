namespace Aegis.Cafeteria.UnitTests.Orders;

using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Domain.Orders;

public sealed class RefundReasonGeneratorTests
{
    [Fact]
    public void Generate_UsesSelectedProductAndOriginalSaleTime()
    {
        DateTimeOffset completedAt = new(2026, 8, 28, 12, 30, 0, TimeSpan.FromHours(-5));
        Order order = new(Guid.NewGuid(), 123, completedAt.AddMinutes(-5));
        OrderItem item = new(1, "Chicken Lunch", 750m, 2);
        order.AddItem(item);
        order.Submit(completedAt.AddMinutes(-1));
        OrderPayment payment = new
        (
              Guid.NewGuid()
            , PaymentMethod.Cash
            , 1_500m
            , completedAt
        );
        order.Complete
        (
              [payment]
            , completedAt
            , "cashier"
            , "POS01"
        );

        string reason = RefundReasonGenerator.Generate(order, [(item.Id, 1)]);

        Assert.Contains("1 x Chicken Lunch", reason);
        Assert.Contains("2026-08-28 12:30", reason);
    }
}
