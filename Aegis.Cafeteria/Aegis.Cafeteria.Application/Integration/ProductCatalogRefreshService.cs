namespace Aegis.Cafeteria.Application.Integration;

using Microsoft.Extensions.Logging;

public sealed class ProductCatalogRefreshService(IProductCatalogSource source, IProductCatalogCache cache, ILogger<ProductCatalogRefreshService> logger)
{
    public async Task<int> RefreshAsync(CancellationToken cancellationToken = default)
    {
        CafeteriaCatalogSnapshot catalog = await source.GetCatalogAsync(cancellationToken);
        await cache.ReplaceCatalogAsync(catalog, cancellationToken);
        logger.LogInformation(
            "FFP LunchItems refresh complete: {ProductCount} sale items; credit policy {CreditCode} Price {CreditPrice}; subsidy policy {SubsidyCode} Cost {SubsidyCost}",
            catalog.Products.Count, catalog.CreditLimitLunchItemCode, catalog.CreditLimitPrice, catalog.SubsidyLunchItemCode, catalog.SubsidyCost);
        return catalog.Products.Count;
    }
}

public static class LunchItemRatePolicyDeriver
{
    public static CafeteriaCatalogSnapshot Derive(
        IReadOnlyCollection<ExternalProductSnapshot> activeLunchItems,
        DateTimeOffset refreshedAt,
        string creditLimitLunchItemCode = "FFP00",
        string subsidyLunchItemCode = "FFP00")
    {
        ArgumentNullException.ThrowIfNull(activeLunchItems);
        if (string.IsNullOrWhiteSpace(creditLimitLunchItemCode)) throw new ArgumentException("CreditLimitLunchItemCode is required.", nameof(creditLimitLunchItemCode));
        if (string.IsNullOrWhiteSpace(subsidyLunchItemCode)) throw new ArgumentException("SubsidyLunchItemCode is required.", nameof(subsidyLunchItemCode));
        string creditCode = creditLimitLunchItemCode.Trim();
        string subsidyCode = subsidyLunchItemCode.Trim();
        ExternalProductSnapshot creditRow = RequirePolicyRow(activeLunchItems, creditCode, "credit-limit", requirePrice: true, requireCost: false);
        ExternalProductSnapshot subsidyRow = RequirePolicyRow(activeLunchItems, subsidyCode, "subsidy", requirePrice: false, requireCost: true);

        HashSet<string> reservedPolicyCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            creditCode,
            subsidyCode
        };

        ExternalProductSnapshot[] saleable = activeLunchItems
            .Where(item => !reservedPolicyCodes.Contains(item.Code.Trim()))
            .Where(item => item.ProductId > 0 && !string.IsNullOrWhiteSpace(item.Name) && item.Price >= 0m)
            .ToArray();
        if (saleable.Length == 0) throw new InvalidOperationException("FFP LunchItems contains no active saleable cafeteria items.");

        return new CafeteriaCatalogSnapshot(saleable, creditCode, creditRow.Price, subsidyCode, subsidyRow.Cost, refreshedAt);
    }

    private static ExternalProductSnapshot RequirePolicyRow(IReadOnlyCollection<ExternalProductSnapshot> items, string code, string purpose, bool requirePrice, bool requireCost)
    {
        ExternalProductSnapshot[] rows = items.Where(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (rows.Length == 0) throw new InvalidOperationException($"FFP LunchItems has no active {purpose} policy row with Code='{code}'.");
        if (rows.Length > 1) throw new InvalidOperationException($"FFP LunchItems contains more than one active {purpose} policy row with Code='{code}'.");
        if (requirePrice && rows[0].Price <= 0m) throw new InvalidOperationException($"{code}.Price must be positive for the credit-limit policy.");
        if (requireCost && rows[0].Cost <= 0m) throw new InvalidOperationException($"{code}.Cost must be positive for the subsidy policy.");
        return rows[0];
    }
}
