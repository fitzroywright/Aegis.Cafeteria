namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class FinancialPostingServiceTests
{
    [Fact]
    public async Task SaveStipendAsync_ClosedPeriod_RejectsWithoutLedgerMutation()
    {
        string file = SqliteTestDatabase.CreatePath("closed-stipend");
        try
        {
            (LocalDbContextFactory factory, EfCafeteriaPersistenceStore store) = await SetupAsync(file);
            await ClosePeriodAsync(factory, 41, 2026, 8);

            Stipend stipend = new(
                Guid.NewGuid(),
                41,
                500m,
                500m,
                StipendPaymentMode.Advance,
                "manager",
                new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero));

            InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.SaveStipendAsync(stipend));

            Assert.Contains("closed cafeteria period 2026-08", exception.Message);

            await using LocalCafeteriaDbContext db = factory.CreateDbContext();
            Assert.Empty(await db.AccountEvents.ToListAsync());
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task SaveStipendAsync_OpenPeriod_Persists()
    {
        string file = SqliteTestDatabase.CreatePath("open-stipend");
        try
        {
            (LocalDbContextFactory factory, EfCafeteriaPersistenceStore store) = await SetupAsync(file);

            Stipend stipend = new(
                Guid.NewGuid(),
                42,
                500m,
                300m,
                StipendPaymentMode.Reimbursement,
                "manager",
                new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));

            await store.SaveStipendAsync(stipend);

            await using LocalCafeteriaDbContext db = factory.CreateDbContext();
            AccountEventRow saved = Assert.Single(await db.AccountEvents.ToListAsync());
            Assert.Equal("Stipend", saved.EventType);
            Assert.Equal(2026, saved.AccountYear);
            Assert.Equal(9, saved.AccountMonth);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task SaveAccountEventAsync_ClosedPeriod_ProtectsAllFinancialEventTypes()
    {
        string file = SqliteTestDatabase.CreatePath("closed-financial-event");
        try
        {
            (LocalDbContextFactory factory, EfCafeteriaPersistenceStore store) = await SetupAsync(file);
            await ClosePeriodAsync(factory, 43, 2026, 8);

            CustomerPayment payment = new(
                Guid.NewGuid(),
                43,
                100m,
                new DateTimeOffset(2026, 8, 31, 13, 0, 0, TimeSpan.Zero),
                reference: "TEST",
                paymentMethod: "Cash",
                receivedByUserId: "manager",
                outstandingBalanceBefore: 100m,
                outstandingBalanceAfter: 0m);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.SaveCustomerPaymentAsync(payment));

            await using LocalCafeteriaDbContext db = factory.CreateDbContext();
            Assert.Empty(await db.AccountEvents.ToListAsync());
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task SaveOrderAsync_ClosedPeriod_RejectsNewSaleWithoutOrderMutation()
    {
        string file = SqliteTestDatabase.CreatePath("closed-sale");
        try
        {
            (LocalDbContextFactory factory, EfCafeteriaPersistenceStore store) = await SetupAsync(file);
            await ClosePeriodAsync(factory, 44, 2026, 8);

            DateTimeOffset at = new(2026, 8, 31, 14, 0, 0, TimeSpan.Zero);
            Order order = new(Guid.NewGuid(), 44, at.AddMinutes(-2));
            order.AddItem(new OrderItem(1, "Lunch", 500m, 1));
            order.Submit(at.AddMinutes(-1));
            order.Complete(
                [new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 500m, at)],
                at,
                "cashier",
                "TEST-01");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.SaveOrderAsync(order));

            await using LocalCafeteriaDbContext db = factory.CreateDbContext();
            Assert.Empty(await db.Orders.ToListAsync());
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    private static async Task<(LocalDbContextFactory Factory, EfCafeteriaPersistenceStore Store)> SetupAsync(string file)
    {
        LocalDbContextFactory factory = new(
            DbContextOptionsFactory.CreateLocal(
                new CafeteriaPersistenceOptions
                {
                    LocalSqliteConnectionString = $"Data Source={file}",
                    TerminalId = "TEST-01"
                }));

        await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
        }

        LocalPersistenceStore localStore = new(factory, NullLogger<LocalPersistenceStore>.Instance);
        return (factory, new EfCafeteriaPersistenceStore(localStore));
    }

    private static async Task ClosePeriodAsync(
        LocalDbContextFactory factory,
        int customerId,
        int year,
        int month)
    {
        await using LocalCafeteriaDbContext db = factory.CreateDbContext();
        db.AccountPeriods.Add(
            new AccountPeriodRow
            {
                CustomerId = customerId,
                Year = year,
                Month = month,
                WorkingDays = 20,
                IsClosed = true,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedBy = "test",
                Revision = DateTimeOffset.UtcNow.UtcDateTime.Ticks,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();
    }
}
