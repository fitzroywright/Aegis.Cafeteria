namespace Aegis.Cafeteria.Application.Integration;

/// <summary>
/// Source-neutral boundary for the FFP-owned cafeteria catalog and funding policy.
/// The configured credit-limit and subsidy LunchItem rows are policy rows, not saleable products.
/// </summary>
public interface IProductCatalogSource
{
    Task<CafeteriaCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed record ExternalProductSnapshot(int ProductId, string Code, string Name, decimal Price, decimal Cost);

public sealed record CafeteriaCatalogSnapshot(
    IReadOnlyList<ExternalProductSnapshot> Products,
    string CreditLimitLunchItemCode,
    decimal CreditLimitPrice,
    string SubsidyLunchItemCode,
    decimal SubsidyCost,
    DateTimeOffset RefreshedAt);

public interface IProductCatalogCache
{
    Task ReplaceCatalogAsync(CafeteriaCatalogSnapshot catalog, CancellationToken cancellationToken = default);
}
