namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfProductCatalogMaintenance
(
      IDbContextFactory<LocalCafeteriaDbContext> factory
    , ILunchItemBootstrapSource? lunchItemSource = null
    , IFfpMonthImportService? monthImportService = null
) : IProductCatalogMaintenance
{
    private const string ReservedPolicyCode = "FFP00";

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

    public async Task<ProductCatalogImportResult> ImportFromFfpAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        if (lunchItemSource is null)
        {
            throw new InvalidOperationException("FFP LunchItems import is not available because Cafeteria Services is not configured.");
        }

        IReadOnlyList<ExternalProductSnapshot> sourceItems = await lunchItemSource.GetLunchItemsAsync(cancellationToken);
        int ignoredFfpProducts = sourceItems.Count(item => IsReservedPolicyRow(item.Code));

        ExternalProductSnapshot[] importable = sourceItems
            .Where(item => !IsReservedPolicyRow(item.Code))
            .Where(item => item.ProductId > 0)
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => item.Price >= 0m)
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        List<ProductRow> existing = await db.Products.ToListAsync(cancellationToken);
        HashSet<string> existingNames = existing
            .Where(product => !string.IsNullOrWhiteSpace(product.Name))
            .Select(product => product.Name.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int existingProducts = 0;
        int addedProducts = 0;
        int nextId = existing.Select(product => product.Id).DefaultIfEmpty(0).Max() + 1;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (ExternalProductSnapshot item in importable)
        {
            string name = item.Name.Trim();
            if (!existingNames.Add(name))
            {
                existingProducts++;
                continue;
            }

            db.Products.Add
            (
                new ProductRow
                {
                      Id = nextId++
                    , Name = name
                    , Price = item.Price
                    , Revision = now.UtcDateTime.Ticks
                    , UpdatedAt = now
                }
            );
            addedProducts++;
        }

        if (addedProducts > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new ProductCatalogImportResult
        (
              addedProducts
            , existingProducts
            , ignoredFfpProducts
        );
    }

    public Task<FfpMonthImportResult> ImportCurrentMonthFromFfpAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        if (monthImportService is null)
        {
            throw new InvalidOperationException("FFP current-month import is not available because Cafeteria Services is not configured.");
        }

        DateTimeOffset now = DateTimeOffset.Now;
        return monthImportService.ImportAsync(now.Year, now.Month, cancellationToken);
    }

    private static bool IsReservedPolicyRow(string? code)
        => string.Equals(code?.Trim(), ReservedPolicyCode, StringComparison.OrdinalIgnoreCase);

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
