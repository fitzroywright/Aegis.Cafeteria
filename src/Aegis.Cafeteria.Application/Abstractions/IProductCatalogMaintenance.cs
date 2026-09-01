namespace Aegis.Cafeteria.Application.Abstractions;

using Aegis.Cafeteria.Application.Integration;

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
    )
    {
        throw new NotSupportedException("FFP LunchItems bootstrap import is not available for this product catalog implementation.");
    }

    Task<FfpMonthImportResult> ImportCurrentMonthFromFfpAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        throw new NotSupportedException("FFP current-month transaction import is not available for this administration implementation.");
    }
}
