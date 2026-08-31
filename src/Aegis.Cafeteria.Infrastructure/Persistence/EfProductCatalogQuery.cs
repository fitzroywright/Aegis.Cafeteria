namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

public sealed class EfProductCatalogQuery(IDbContextFactory<LocalCafeteriaDbContext> factory) : IProductCatalogQuery
{
    public async Task<IReadOnlyList<ProductCatalogItem>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Products.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ProductCatalogItem(x.Id, x.Name, x.Price))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProductCatalogItem>> SearchAsync(string searchText, int maximumResults = 50, CancellationToken cancellationToken = default)
    {
        if (maximumResults <= 0) return [];
        var normalized = (searchText ?? string.Empty).Trim().ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.Products.AsNoTracking();
        if (normalized.Length > 0)
            query = query.Where(x => x.Name.ToUpper().Contains(normalized));
        return await query.OrderBy(x => x.Name)
            .Take(maximumResults)
            .Select(x => new ProductCatalogItem(x.Id, x.Name, x.Price))
            .ToListAsync(cancellationToken);
    }
}
