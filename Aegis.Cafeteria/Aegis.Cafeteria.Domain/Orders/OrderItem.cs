namespace Aegis.Cafeteria.Domain.Orders;

using Aegis.Cafeteria.Domain.Products;

public sealed class OrderItem
{
    public OrderItem(Guid id, int productId, string productName, decimal unitPrice, int quantity)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Order item ID is required.", nameof(id));
        if (productId <= 0)
            throw new ArgumentOutOfRangeException(nameof(productId));
        if (string.IsNullOrWhiteSpace(productName))
            throw new ArgumentException("Product name is required.", nameof(productName));
        if (unitPrice < 0)
            throw new ArgumentOutOfRangeException(nameof(unitPrice));
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        Id = id;
        ProductId = productId;
        ProductName = productName.Trim();
        UnitPrice = unitPrice;
        Quantity = quantity;
    }

    public OrderItem(int productId, string productName, decimal unitPrice, int quantity)
        : this(Guid.NewGuid(), productId, productName, unitPrice, quantity)
    {
    }

    public Guid Id { get; }
    public int ProductId { get; }
    public string ProductName { get; }
    public decimal UnitPrice { get; }
    public int Quantity { get; }
    public decimal Total => UnitPrice * Quantity;

    public static OrderItem FromProduct(Product product, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(product);
        return new OrderItem(Guid.NewGuid(), product.Id, product.Name, product.Price, quantity);
    }
}
