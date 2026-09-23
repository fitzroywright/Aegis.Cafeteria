namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfProductCatalogMaintenance
(
    IDbContextFactory<LocalCafeteriaDbContext> factory
) : IProductCatalogMaintenance
{

    public async Task<IReadOnlyList<ProductCatalogItem>> GetAllAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);

        return await db.Products
            .AsNoTracking()
            .OrderBy(product => product.Name)
            .Select(product => new ProductCatalogItem(product.Id, product.Name, product.Price))
            .ToListAsync(cancellationToken);
    }

    public async Task<ProductCatalogItem> AddAsync
    (
          string name
        , decimal price
        , CancellationToken cancellationToken = default
    )
    {
        Validate(name, price);

        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        int nextId = (await db.Products.Select(product => (int?)product.Id).MaxAsync(cancellationToken) ?? 0) + 1;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ProductRow row = new()
        {
              Id = nextId
            , Name = name.Trim()
            , Price = price
            , Revision = now.UtcDateTime.Ticks
            , UpdatedAt = now
        };

        db.Products.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return new ProductCatalogItem(row.Id, row.Name, row.Price);
    }

    public async Task<ProductCatalogItem> UpdateAsync
    (
          int productId
        , string name
        , decimal price
        , CancellationToken cancellationToken = default
    )
    {
        if (productId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId));
        }

        Validate(name, price);

        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        ProductRow row = await db.Products.SingleOrDefaultAsync(product => product.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException($"Product {productId} was not found.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        row.Name = name.Trim();
        row.Price = price;
        row.Revision = now.UtcDateTime.Ticks;
        row.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        return new ProductCatalogItem(row.Id, row.Name, row.Price);
    }

    public async Task DeleteAsync
    (
          int productId
        , CancellationToken cancellationToken = default
    )
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        ProductRow? row = await db.Products.SingleOrDefaultAsync(product => product.Id == productId, cancellationToken);

        if (row is null)
        {
            return;
        }

        db.Products.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void Validate
    (
          string name
        , decimal price
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Product name is required.", nameof(name));
        }

        if (price < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Product price cannot be negative.");
        }
    }
}
