namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class FfpLedgerOutboxTests
{
    [Fact]
    public async Task Enqueue_IsDurableAndIdempotentByOperationId()
    {
        var file=SqliteTestDatabase.CreatePath("ffp-outbox");
        try
        {
            var options=new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}"};
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using(var create=factory.CreateDbContext()) await create.Database.EnsureCreatedAsync();
            var outbox=new EfFfpLedgerOutbox(factory); var id=Guid.NewGuid();
            var entry=new ExternalLedgerEntry(id,101,ExternalLedgerEntryKind.Purchase,DateTimeOffset.UtcNow,"Lunch x1 - Total: 500.00",500m,5000m,4500m,"PURCHASE","FWright","Cafeteria Purchase");
            await outbox.EnqueueAsync(entry); await outbox.EnqueueAsync(entry);
            await using var db=await factory.CreateDbContextAsync();
            Assert.Equal(1,await db.FfpLedgerOutbox.CountAsync());
            var row=Assert.Single(await db.FfpLedgerOutbox.ToListAsync());
            Assert.Equal("FWright",row.UserName);
            Assert.Equal("Cafeteria Purchase",row.IdName);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }
}
