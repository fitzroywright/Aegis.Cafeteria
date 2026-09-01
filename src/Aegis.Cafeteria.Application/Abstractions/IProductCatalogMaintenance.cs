namespace Aegis.Cafeteria.Application.Abstractions;

public sealed record ProductCatalogImportResult
(
      int AddedProducts
    , int ExistingProducts
    , int IgnoredFfpProducts
);

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

    Task<ProductCatalogImportResult> ImportFromFfpAsync
    (
        CancellationToken cancellationToken = default
    );
}
