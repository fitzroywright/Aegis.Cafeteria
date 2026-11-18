namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class ProductCatalogRefreshTests
{
    [Fact]
    public async Task Refresh_CachesSaleItemsAndSeparateCreditAndSubsidyPolicy()
    {
        string file=SqliteTestDatabase.CreatePath("product-refresh"); try { CafeteriaPersistenceOptions options=new(){LocalSqliteConnectionString=$"Data Source={file}"}; LocalDbContextFactory factory=new(DbContextOptionsFactory.CreateLocal(options)); await using(LocalCafeteriaDbContext create=factory.CreateDbContext()) await create.Database.EnsureCreatedAsync(); CafeteriaCatalogSnapshot catalog=new([new ExternalProductSnapshot(10,"LUNCH","Lunch",800m,800m),new ExternalProductSnapshot(11,"JUICE","Juice",200m,200m)],"FFP00",800m,"FFP00",250m,DateTimeOffset.UtcNow); ProductCatalogRefreshService service=new(new Source(catalog),new EfProductCatalogCache(factory),NullLogger<ProductCatalogRefreshService>.Instance); Assert.Equal(2,await service.RefreshAsync()); await using LocalCafeteriaDbContext db=await factory.CreateDbContextAsync(); Assert.Equal(2,await db.Products.CountAsync()); Assert.Equal(800m,(await db.Products.SingleAsync(product=>product.Id==10)).Price); CafeteriaRatePolicyRow policy=Assert.Single(await db.CafeteriaRatePolicies.ToListAsync()); Assert.Equal("FFP00",policy.CreditLimitLunchItemCode); Assert.Equal(800m,policy.CreditLimitPrice); Assert.Equal("FFP00",policy.SubsidyLunchItemCode); Assert.Equal(250m,policy.SubsidyCost); } finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public void Derive_ExcludesAllFfpPrefixedRowsFromSaleCatalog()
    {
        CafeteriaCatalogSnapshot catalog=LunchItemRatePolicyDeriver.Derive([new ExternalProductSnapshot(100,"FFP00","Policy",800m,250m),new ExternalProductSnapshot(101,"FFP01","System Policy",850m,500m),new ExternalProductSnapshot(102,"FFP-JUICE","System Juice",200m,100m),new ExternalProductSnapshot(103,"LUNCH","Lunch",800m,500m)],DateTimeOffset.UtcNow);
        Assert.Single(catalog.Products); Assert.Equal("LUNCH",catalog.Products[0].Code); Assert.DoesNotContain(catalog.Products,product=>product.Code.StartsWith("FFP",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Cache_RejectsReservedProductsBeforeReplacingTheExistingCatalogOrPolicy()
    {
        string file = SqliteTestDatabase.CreatePath("reserved-product-cache");
        try
        {
            var factory = new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(
                new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Data Source={file}" }));
            await using var db = await factory.CreateDbContextAsync();
            await db.Database.EnsureCreatedAsync();
            var cache = new EfProductCatalogCache(factory);
            var lunch = new ExternalProductSnapshot(10, "LUNCH", "Lunch", 800m, 500m);
            var catalog = new CafeteriaCatalogSnapshot([lunch], "FFP00", 800m, "FFP00", 250m, DateTimeOffset.UtcNow);
            await cache.ReplaceCatalogAsync(catalog);

            await Assert.ThrowsAsync<ArgumentException>(() => cache.ReplaceCatalogAsync(catalog with
            {
                Products = [new(11, "\t fFp-JUICE ", "Policy Juice", 200m, 100m)],
                CreditLimitPrice = 999m,
                SubsidyCost = 999m
            }));

            Assert.Equal(lunch.ProductId, Assert.Single(await db.Products.ToArrayAsync()).Id);
            var policy = Assert.Single(await db.CafeteriaRatePolicies.ToArrayAsync());
            Assert.Equal(800m, policy.CreditLimitPrice);
            Assert.Equal(250m, policy.SubsidyCost);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    private sealed class Source(CafeteriaCatalogSnapshot catalog):IProductCatalogSource { public Task<CafeteriaCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken=default)=>Task.FromResult(catalog); }
}
