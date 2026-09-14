namespace Aegis.Cafeteria.Domain.Products;

public sealed class Product
{
    public Product(int id, string name, decimal price)
    {
        if (id <= 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name is required.", nameof(name));
        if (price < 0)
            throw new ArgumentOutOfRangeException(nameof(price));

        Id = id;
        Name = name.Trim();
        Price = price;
    }

    public int Id { get; }
    public string Name { get; }
    public decimal Price { get; }
}
