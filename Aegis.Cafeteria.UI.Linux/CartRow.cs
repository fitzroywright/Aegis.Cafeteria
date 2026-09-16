namespace Aegis.Cafeteria.UI.Linux;

internal sealed class CartRow(int productId, string productName, decimal unitPrice, int quantity)
{
    public int ProductId { get; } = productId;
    public string ProductName { get; } = productName;
    public decimal UnitPrice { get; } = unitPrice;
    public int Quantity { get; set; } = quantity;
    public decimal Total => UnitPrice * Quantity;
}
