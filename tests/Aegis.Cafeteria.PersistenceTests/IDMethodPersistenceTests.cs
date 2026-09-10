namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class IDMethodPersistenceTests
{
    [Theory]
    [InlineData(IDMethod.NFC, "04A1B2C3")]
    [InlineData(IDMethod.Code, "EMP0042")]
    [InlineData(IDMethod.TRN, "123456789")]
    [InlineData(IDMethod.Name, "NAME SEARCH")]
    public async Task SaveOrder_PersistsAndRestoresIdentificationContext(IDMethod method, string code)
    {
        string file = SqliteTestDatabase.CreatePath("idmethod-order");
        try
        {
            CafeteriaPersistenceOptions options = new()
            {
                LocalSqliteConnectionString = $"Data Source={file}",
                TerminalId = "TEST"
            };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await using (LocalCafeteriaDbContext create = factory.CreateDbContext())
            {
                await create.Database.EnsureCreatedAsync();
            }

            EfCafeteriaPersistenceStore store = new(
                new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            Order order = new(Guid.NewGuid(), 42, DateTimeOffset.UtcNow, method, code);
            order.AddItem(new OrderItem(1, "Lunch", 500m, 1));
            order.Submit(DateTimeOffset.UtcNow);
            order.Complete(
                [new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 500m, DateTimeOffset.UtcNow)],
                DateTimeOffset.UtcNow,
                "fwright",
                "POS01",
                "Fitzroy Wright");

            await store.SaveOrderAsync(order);

            await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync();
            var row = await db.Orders.AsNoTracking()
                .Include(x => x.Items)
                .Include(x => x.Payments)
                .Include(x => x.Refunds)
                .SingleAsync(x => x.Id == order.Id);
            Assert.Equal((int)method, row.IDMethod);
            Assert.Equal(code, row.IdentificationCode);
            Assert.Equal("Fitzroy Wright", row.CashierName);

            Order restored = DomainReadMapper.ToDomain(row);
            Assert.Equal(method, restored.IDMethod);
            Assert.Equal(code, restored.IdentificationCode);
            Assert.Equal("Fitzroy Wright", restored.CashierName);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }
}
