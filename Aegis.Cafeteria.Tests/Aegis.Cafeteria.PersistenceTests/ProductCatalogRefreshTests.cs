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
        string file = SqliteTestDatabase.CreatePath("product-refresh");
        try
        {
            CafeteriaPersistenceOptions options = new() { LocalSqliteConnectionString = $"Data Source={file}" };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await using (LocalCafeteriaDbContext create = factory.CreateDbContext()) await create.Database.EnsureCreatedAsync();

            CafeteriaCatalogSnapshot catalog = new
            (
                [
                      new ExternalProductSnapshot(10, "LUNCH", "Lunch", 800m, 800m)
                    , new ExternalProductSnapshot(11, "JUICE", "Juice", 200m, 200m)
                ],
                CreditLimitLunchItemCode: "FFP00",
                CreditLimitPrice: 800m,
                SubsidyLunchItemCode: "FFP00",
                SubsidyCost: 250m,
                RefreshedAt: DateTimeOffset.UtcNow
            );

            ProductCatalogRefreshService service = new
            (
                  new Source(catalog)
                , new EfProductCatalogCache(factory)
                , NullLogger<ProductCatalogRefreshService>.Instance
            );

            Assert.Equal(2, await service.RefreshAsync());
            await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync();
            Assert.Equal(2, await db.Products.CountAsync());
            Assert.Equal(800m, (await db.Products.SingleAsync(product => product.Id == 10)).Price);

            CafeteriaRatePolicyRow policy = Assert.Single(await db.CafeteriaRatePolicies.ToListAsync());
            Assert.Equal("FFP00", policy.CreditLimitLunchItemCode);
            Assert.Equal(800m, policy.CreditLimitPrice);
            Assert.Equal("FFP00", policy.SubsidyLunchItemCode);
            Assert.Equal(250m, policy.SubsidyCost);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public void Derive_ExcludesOnlyReservedPolicyCode_NotEveryFfpPrefix()
    {
        CafeteriaCatalogSnapshot catalog = LunchItemRatePolicyDeriver.Derive
        (
            [
                  new ExternalProductSnapshot(100, "FFP00", "Policy", 800m, 250m)
                , new ExternalProductSnapshot(101, "FFP01", "Lunch Special", 850m, 500m)
                , new ExternalProductSnapshot(102, "FFP-JUICE", "Juice", 200m, 100m)
            ],
            DateTimeOffset.UtcNow
        );

        Assert.Equal(2, catalog.Products.Count);
        Assert.DoesNotContain(catalog.Products, product => product.Code.Equals("FFP00", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(catalog.Products, product => product.Code == "FFP01");
        Assert.Contains(catalog.Products, product => product.Code == "FFP-JUICE");
    }

    [Fact]
    public async Task BootstrapImport_AddsMissingProducts_AndIgnoresOnlyReservedFfpPolicyRow()
    {
        string file = SqliteTestDatabase.CreatePath("product-bootstrap");
        try
        {
            CafeteriaPersistenceOptions options = new() { LocalSqliteConnectionString = $"Data Source={file}" };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await using (LocalCafeteriaDbContext create = factory.CreateDbContext())
            {
                await create.Database.EnsureCreatedAsync();
                create.Products.Add
                (
                    new ProductRow
                    {
                          Id = 1
                        , Name = "Lunch"
                        , Price = 999m
                        , Revision = 1
                        , UpdatedAt = DateTimeOffset.UtcNow
                    }
                );
                await create.SaveChangesAsync();
            }

            ILunchItemBootstrapSource source = new BootstrapSource
            (
                [
                      new ExternalProductSnapshot(100, "FFP00", "Policy", 800m, 600m)
                    , new ExternalProductSnapshot(101, "FFPTEST", "System Test", 50m, 10m)
                    , new ExternalProductSnapshot(102, "LUNCH", "Lunch", 800m, 500m)
                    , new ExternalProductSnapshot(103, "JUICE", "Juice", 200m, 100m)
                ]
            );
            EfProductCatalogMaintenance maintenance = new(factory, source);

            ProductCatalogImportResult result = await maintenance.ImportFromFfpAsync();

            Assert.Equal(2, result.AddedProducts);
            Assert.Equal(1, result.ExistingProducts);
            Assert.Equal(1, result.IgnoredFfpProducts);

            await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync();
            Assert.Equal(3, await db.Products.CountAsync());
            Assert.Equal(999m, (await db.Products.SingleAsync(product => product.Name == "Lunch")).Price);
            Assert.Equal(200m, (await db.Products.SingleAsync(product => product.Name == "Juice")).Price);
            Assert.Equal(50m, (await db.Products.SingleAsync(product => product.Name == "System Test")).Price);
            Assert.DoesNotContain(await db.Products.ToListAsync(), product => product.Name == "Policy");
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    private sealed class Source(CafeteriaCatalogSnapshot catalog) : IProductCatalogSource
    {
        public Task<CafeteriaCatalogSnapshot> GetCatalogAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(catalog);
    }

    private sealed class BootstrapSource(IReadOnlyList<ExternalProductSnapshot> items) : ILunchItemBootstrapSource
    {
        public Task<IReadOnlyList<ExternalProductSnapshot>> GetLunchItemsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(items);
    }
}
