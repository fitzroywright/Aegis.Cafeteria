namespace Aegis.Cafeteria.Application.Abstractions;

public interface IProductCatalogMaintenance
{
    Task<IReadOnlyList<ProductCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<ProductCatalogItem> AddAsync
    (
          string name
        , decimal price
        , CancellationToken cancellationToken = default
    );

    Task<ProductCatalogItem> UpdateAsync
    (
          int productId
        , string name
        , decimal price
        , CancellationToken cancellationToken = default
    );

    Task DeleteAsync
    (
          int productId
        , CancellationToken cancellationToken = default
    );

}
