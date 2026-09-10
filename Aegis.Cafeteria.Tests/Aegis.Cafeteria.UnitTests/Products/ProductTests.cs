namespace Aegis.Cafeteria.UnitTests.Products;

using Aegis.Cafeteria.Domain.Products;

public sealed class ProductTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var product = new Product(1, "Lunch", 650m);
        Assert.Equal(1, product.Id);
        Assert.Equal("Lunch", product.Name);
        Assert.Equal(650m, product.Price);
    }

    [Fact]
    public void Constructor_AllowsZeroPrice()
    {
        var product = new Product(1, "Water", 0m);
        Assert.Equal(0m, product.Price);
    }

    [Fact]
    public void Constructor_WhenPriceNegative_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Product(1, "Lunch", -1m));
    }
}
