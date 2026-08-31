namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class LocalProductCatalogMaintenanceTests
{
    [Fact]
    public async Task AddUpdateDelete_ChangesOnlyCurrentLocalCatalog()
    {
        string file = SqliteTestDatabase.CreatePath("local-products");

        try
        {
            LocalDbContextFactory factory = new
            (
                DbContextOptionsFactory.CreateLocal
                (
                    new CafeteriaPersistenceOptions
                    {
                        LocalSqliteConnectionString = $"Data Source={file}"
                    }
                )
            );

            await using (LocalCafeteriaDbContext database = factory.CreateDbContext())
            {
                await database.Database.EnsureCreatedAsync();
            }

            EfProductCatalogMaintenance maintenance = new(factory);
            Aegis.Cafeteria.Application.Abstractions.ProductCatalogItem created = await maintenance.AddAsync("Lunch", 650m);
            Assert.Equal(650m, created.Price);

            Aegis.Cafeteria.Application.Abstractions.ProductCatalogItem updated = await maintenance.UpdateAsync(created.Id, "Chicken Lunch", 700m);
            Assert.Equal("Chicken Lunch", updated.Name);
            Assert.Equal(700m, updated.Price);

            await maintenance.DeleteAsync(created.Id);
            Assert.Empty(await maintenance.GetAllAsync());
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }
}
