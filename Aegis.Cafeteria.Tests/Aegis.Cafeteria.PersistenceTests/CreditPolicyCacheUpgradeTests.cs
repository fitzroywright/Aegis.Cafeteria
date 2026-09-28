namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class CreditPolicyCacheUpgradeTests
{
    [Fact]
    public async Task ExistingCache_AddsPolicyVersionWithoutChangingCustomerCredit()
    {
        string file = SqliteTestDatabase.CreatePath("credit-policy-upgrade");
        try
        {
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Data Source={file}" }));
            PersistenceInitializer initializer = new(factory, NullLogger<PersistenceInitializer>.Instance);
            await initializer.InitializeLocalAsync();
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                db.Customers.Add(new CustomerRow { Id = 1, Name = "Employee", AdditionalCreditLimit = 700m });
                await db.SaveChangesAsync();
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE Customers DROP COLUMN AdditionalCreditPolicyUpdatedAt;");
            }
            await initializer.InitializeLocalAsync();
            await initializer.InitializeLocalAsync();
            await using LocalCafeteriaDbContext upgraded = factory.CreateDbContext();
            CustomerRow customer = await upgraded.Customers.SingleAsync();
            Assert.Equal(700m, customer.AdditionalCreditLimit);
            Assert.Null(customer.AdditionalCreditPolicyUpdatedAt);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }
}
