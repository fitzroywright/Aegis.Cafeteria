namespace Aegis.Cafeteria.Application.Abstractions;

public interface IProductCatalogQuery
{
    Task<IReadOnlyList<ProductCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ProductCatalogItem>> SearchAsync(string searchText, int maximumResults = 50, CancellationToken cancellationToken = default);
}

public sealed record ProductCatalogItem(int Id, string Name, decimal Price);
