namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

public sealed class ProductCatalogRefreshTests
{
    [Fact]
    public async Task Refresh_CachesSaleItemsAndSeparateCreditAndSubsidyPolicy()
    {
        var file = SqliteTestDatabase.CreatePath("product-refresh");
        try
        {
            var options = new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Data Source={file}" };
            var factory = new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using (var create = factory.CreateDbContext()) await create.Database.EnsureCreatedAsync();

            var catalog = new CafeteriaCatalogSnapshot(
                [
                    new ExternalProductSnapshot(10, "LUNCH", "Lunch", 800m, 800m),
                    new ExternalProductSnapshot(11, "JUICE", "Juice", 200m, 200m)
                ],
                CreditLimitLunchItemCode: "FFP00",
                CreditLimitPrice: 800m,
                SubsidyLunchItemCode: "FFP00",
                SubsidyCost: 250m,
                RefreshedAt: DateTimeOffset.UtcNow);

            var service = new ProductCatalogRefreshService(
                new Source(catalog),
                new EfProductCatalogCache(factory),
                NullLogger<ProductCatalogRefreshService>.Instance);

            Assert.Equal(2, await service.RefreshAsync());
            await using var db = await factory.CreateDbContextAsync();
            Assert.Equal(2, await db.Products.CountAsync());
            Assert.Equal(800m, (await db.Products.SingleAsync(x => x.Id == 10)).Price);

            var policy = Assert.Single(await db.CafeteriaRatePolicies.ToListAsync());
            Assert.Equal("FFP00", policy.CreditLimitLunchItemCode);
            Assert.Equal(800m, policy.CreditLimitPrice);
            Assert.Equal("FFP00", policy.SubsidyLunchItemCode);
            Assert.Equal(250m, policy.SubsidyCost);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    private sealed class Source(CafeteriaCatalogSnapshot catalog) : IProductCatalogSource
    {
        public Task<CafeteriaCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(catalog);
    }
}
