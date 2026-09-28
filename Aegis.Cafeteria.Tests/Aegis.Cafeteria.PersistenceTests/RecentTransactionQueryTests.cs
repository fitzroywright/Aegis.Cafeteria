using Aegis.Cafeteria.Domain.Accounts;
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
        string file = SqliteTestDatabase.CreatePath("recent-transactions");
        try
        {
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(new CafeteriaPersistenceOptions
            {
                LocalSqliteConnectionString = $"Data Source={file}",
                TerminalId = "POS-TEST"
            }));
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            EfCafeteriaPersistenceStore store = new(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(100, "Jane Employee", null, true, 0m));

            DateTimeOffset now = DateTimeOffset.Now;
            await store.SaveMonthlyCreditAllocationAsync(new MonthlyCreditAllocation(100, now.Year, now.Month, 1, true, 1000m, 600m, now));
            Order order = new(Guid.NewGuid(), 100, now.AddMinutes(-2));
            order.AddItem(new OrderItem(Guid.NewGuid(), 10, "Lunch", 750m, 1));
            order.Submit(now.AddMinutes(-1));
            order.Complete(
            [
                new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 400m, now),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 200m, now),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Credit, 150m, now)
            ], now, "cashier1", "POS-TEST");
            await store.SaveOrderAsync(order);

            EfRecentTransactionQuery query = new(factory);
            IReadOnlyList<Aegis.Cafeteria.Application.Abstractions.RecentTransactionView> rows = await query.GetTodayAsync(10, DateOnly.FromDateTime(DateTime.Today));

            Aegis.Cafeteria.Application.Abstractions.RecentTransactionView row = Assert.Single(rows);
            Assert.Equal("Jane Employee", row.CustomerName);
            Assert.Equal(750m, row.Total);
            Assert.Equal(400m, row.CafeteriaAccountAmount);
            Assert.Equal(200m, row.CashAmount);
            Assert.Equal("Card + Cash + Credit", row.TenderSummary);
            Assert.Equal("cashier1", row.CashierUserId);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }
}
