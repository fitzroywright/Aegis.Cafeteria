using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aegis.Cafeteria.PersistenceTests;

public sealed class EmployeeParticipationQueryTests
{
    [Fact]
    public async Task GetWeekAsync_ComparesUniqueEmployeesServedWithEligiblePopulation()
    {
        string file = SqliteTestDatabase.CreatePath("employee-participation");
        try
        {
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(new CafeteriaPersistenceOptions
            {
                LocalSqliteConnectionString = $"Data Source={file}",
                TerminalId = "POS-TEST"
            }));
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
            }

            EfCafeteriaPersistenceStore store = new(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(100, "Eligible One", null, true, 0m));
            await store.SaveCustomerAsync(new Customer(101, "Eligible Two", null, true, 0m));
            await store.SaveCustomerAsync(new Customer(102, "Not Eligible", null, false, 0m));

            DateOnly monday = new(2026, 8, 31);
            await SaveCompletedOrderAsync(store, 100, monday, 8);
            await SaveCompletedOrderAsync(store, 100, monday, 12);
            await SaveCompletedOrderAsync(store, 101, monday.AddDays(1), 12);
            await SaveCompletedOrderAsync(store, 102, monday.AddDays(2), 12);

            EfEmployeeParticipationQuery query = new(factory);
            EmployeeParticipationWeekView result = await query.GetWeekAsync(monday);

            Assert.Equal(2, result.EligibleEmployeeCount);
            Assert.Equal(5, result.Days.Count);
            Assert.Equal(1, result.Days[0].EmployeesServed);
            Assert.Equal(1, result.Days[1].EmployeesServed);
            Assert.Equal(0, result.Days[2].EmployeesServed);
            Assert.Equal(0, result.Days[3].EmployeesServed);
            Assert.Equal(0, result.Days[4].EmployeesServed);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    private static async Task SaveCompletedOrderAsync
    (
        EfCafeteriaPersistenceStore store,
        int customerId,
        DateOnly day,
        int hour
    )
    {
        DateTimeOffset completedAt = new(day.Year, day.Month, day.Day, hour, 0, 0, TimeSpan.Zero);
        Order order = new(Guid.NewGuid(), customerId, completedAt.AddMinutes(-2));
        order.AddItem(new OrderItem(Guid.NewGuid(), 10, "Lunch", 750m, 1));
        order.Submit(completedAt.AddMinutes(-1));
        order.Complete
        (
            [new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 750m, completedAt)],
            completedAt,
            "cashier1",
            "POS-TEST"
        );
        await store.SaveOrderAsync(order);
    }
}
