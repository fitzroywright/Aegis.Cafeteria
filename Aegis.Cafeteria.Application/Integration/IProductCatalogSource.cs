namespace Aegis.Cafeteria.Application.Integration;

/// <summary>
/// Legacy catalog/policy refresh boundary. Saleable products are owned by the POS database.
/// External LunchItems may be used as an explicit bootstrap source, not as the ongoing POS catalog owner.
/// </summary>
public interface IProductCatalogSource
{
    Task<CafeteriaCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed record ExternalProductSnapshot(
    int ProductId,
    string Code,
    string Name,
    decimal Price,
    decimal Cost);

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
