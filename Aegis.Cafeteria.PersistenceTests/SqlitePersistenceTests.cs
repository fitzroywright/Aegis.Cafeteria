namespace Aegis.Cafeteria.PersistenceTests;
using Aegis.Cafeteria.Domain.Customers; using Aegis.Cafeteria.Infrastructure.Configuration; using Aegis.Cafeteria.Infrastructure.Persistence; using Microsoft.EntityFrameworkCore; using Microsoft.Extensions.Logging.Abstractions;
public sealed class SqlitePersistenceTests
{
 [Fact] public async Task SaveCustomer_CommitsLocallyWithoutCentralOutbox(){var file=SqliteTestDatabase.CreatePath("local-save");try{var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}"}));await using(var db=factory.CreateDbContext())await db.Database.EnsureCreatedAsync();var store=new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory,NullLogger<LocalPersistenceStore>.Instance));await store.SaveCustomerAsync(new Customer(1,"Employee",null,true,0m));await using var verify=factory.CreateDbContext();Assert.Single(await verify.Customers.ToListAsync());Assert.Null(verify.Model.FindEntityType("Aegis.Cafeteria.Infrastructure.Persistence.Models.OutboxRow"));}finally{SqliteTestDatabase.Delete(file);}}
}
