using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aegis.Cafeteria.PersistenceTests;

public sealed class RecentTransactionQueryTests
{
    [Fact]
    public async Task GetTodayAsync_ReturnsNewestCompletedOrdersWithCustomerAndTender()
    {
        var file = SqliteTestDatabase.CreatePath("recent-transactions");
        try
        {
            var factory = new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(new CafeteriaPersistenceOptions
            {
                LocalSqliteConnectionString = $"Data Source={file}",
                TerminalId = "POS-TEST"
            }));
            await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var store = new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(100, "Jane Employee", null, true, 0m));

            var now = DateTimeOffset.Now;
            var order = new Order(Guid.NewGuid(), 100, now.AddMinutes(-2));
            order.AddItem(new OrderItem(Guid.NewGuid(), 10, "Lunch", 750m, 1));
            order.Submit(now.AddMinutes(-1));
            order.Complete([new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 750m, now)], now, "cashier1", "POS-TEST");
            await store.SaveOrderAsync(order);

            var query = new EfRecentTransactionQuery(factory);
            var rows = await query.GetTodayAsync(10, DateOnly.FromDateTime(DateTime.Today));

            var row = Assert.Single(rows);
            Assert.Equal("Jane Employee", row.CustomerName);
            Assert.Equal(750m, row.Total);
            Assert.Equal(750m, row.CashAmount);
            Assert.Equal("Cash", row.TenderSummary);
            Assert.Equal("cashier1", row.CashierUserId);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }
}
