namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfProductCatalogCache(IDbContextFactory<LocalCafeteriaDbContext> factory) : IProductCatalogCache
{
    public async Task ReplaceCatalogAsync(CafeteriaCatalogSnapshot catalog, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = catalog.RefreshedAt;
        var existing = await db.Products.ToListAsync(cancellationToken);
        var sourceIds = catalog.Products.Select(x => x.ProductId).ToHashSet();

        db.Products.RemoveRange(existing.Where(x => !sourceIds.Contains(x.Id)));

        foreach (var product in catalog.Products)
        {
            var row = existing.SingleOrDefault(x => x.Id == product.ProductId);
            if (row is null)
            {
                row = new ProductRow { Id = product.ProductId };
                db.Products.Add(row);
                existing.Add(row);
            }

            row.Name = product.Name.Trim();
            row.Price = product.Price; // FFP LunchItems.Price; never Cost.
            row.Revision = now.UtcDateTime.Ticks;
            row.UpdatedAt = now;
        }

        var policy = await db.CafeteriaRatePolicies.SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        if (policy is null)
        {
            policy = new CafeteriaRatePolicyRow { Id = 1 };
            db.CafeteriaRatePolicies.Add(policy);
        }
        policy.CreditLimitLunchItemCode = catalog.CreditLimitLunchItemCode;
        policy.CreditLimitPrice = catalog.CreditLimitPrice;
        policy.SubsidyLunchItemCode = catalog.SubsidyLunchItemCode;
        policy.SubsidyCost = catalog.SubsidyCost;
        policy.RefreshedAt = catalog.RefreshedAt;

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }
}
