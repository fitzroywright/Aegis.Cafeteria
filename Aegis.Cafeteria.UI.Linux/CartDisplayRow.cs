namespace Aegis.Cafeteria.UI.Linux;

internal sealed record CartDisplayRow(int ProductId, string ProductName, decimal UnitPrice, int Quantity, decimal Total)
{
    public override string ToString() => $"{Quantity} x {ProductName} @ {UnitPrice:C2} = {Total:C2}";
}
