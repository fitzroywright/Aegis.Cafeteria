namespace Aegis.Cafeteria.UnitTests.Orders;

using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;

public sealed class OrderItemTests
{
    [Fact]
    public void FromProduct_SnapshotsNameAndPrice()
    {
        var product = new Product(1, "Lunch", 650m);
        var item = OrderItem.FromProduct(product, 2);

        Assert.Equal(1, item.ProductId);
        Assert.Equal("Lunch", item.ProductName);
        Assert.Equal(650m, item.UnitPrice);
        Assert.Equal(2, item.Quantity);
        Assert.Equal(1_300m, item.Total);
    }

    [Fact]
    public void Constructor_WhenQuantityZero_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderItem(1, "Lunch", 650m, 0));
    }
}
