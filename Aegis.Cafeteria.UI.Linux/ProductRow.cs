using Aegis.Cafeteria.Application.Abstractions;

namespace Aegis.Cafeteria.UI.Linux;

internal sealed record ProductRow(ProductCatalogItem Product)
{
    public override string ToString() => $"{Product.Name} - {Product.Price:C2}";
}
