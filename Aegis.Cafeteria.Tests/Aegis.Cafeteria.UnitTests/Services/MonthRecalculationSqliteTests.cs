namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Reporting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

public sealed class MonthRecalculationSqliteTests
{
    [Fact]
    public async Task LedgerDateRange_WorksOnSqlite_AndGuardStillBlocksFinancialActivity()
    {
        string file = Path.Combine(Path.GetTempPath(), $"recalculation-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new Factory(new DbContextOptionsBuilder<ServiceDbContext>()
                .UseSqlite($"Data Source={file}").Options);
            DateTimeOffset start = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
            DateTimeOffset end = start.AddMonths(1);
            await using (var db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.Events.AddRange(
                    Allocation(start),
                    Allocation(end.AddTicks(-1)),
                    Allocation(start.AddTicks(-1)),
                    Allocation(end),
                    // Same instant as the inclusive lower bound, with a different offset.
                    Allocation(start.ToOffset(TimeSpan.FromHours(-5))),
                    Credit(start.AddTicks(-1)),
                    Credit(end));
                await db.SaveChangesAsync();
            }

            var query = new CafeteriaLedgerQuery(factory);
            var lines = await query.GetAsync(start, end);
            Assert.Equal(3, lines.Count);
            Assert.All(lines, line => Assert.True(line.OccurredAt >= start && line.OccurredAt < end));
            var guard = new CafeteriaMonthRecalculationGuard(query);
            Assert.True((await guard.CheckAsync(2026, 10)).IsSafe);

            await using (var db = factory.CreateDbContext())
            {
                db.Events.Add(Credit(start.AddDays(1)));
                await db.SaveChangesAsync();
            }
            var blocked = await guard.CheckAsync(2026, 10);
            Assert.False(blocked.IsSafe);
            Assert.Equal(1, blocked.ActivityCount);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (string suffix in new[] { "", "-wal", "-shm" })
                File.Delete(file + suffix);
        }
    }

    private static ServiceEventRow Allocation(DateTimeOffset at)
    {
        Guid id = Guid.NewGuid();
        var payload = new MonthlyAllocationServiceEvent(id, 7, 2026, 10, 21, true, 800m, 600m, at, "Test");
        return Row(id, CafeteriaServiceEventKind.MonthlyAllocation, at, payload);
    }

    private static ServiceEventRow Credit(DateTimeOffset at)
    {
        Guid id = Guid.NewGuid();
        var payload = new AdditionalCreditServiceEvent(id, 7, 100m, "manager", at, "Test", 100m);
        return Row(id, CafeteriaServiceEventKind.AdditionalCredit, at, payload);
    }

    private static ServiceEventRow Row<T>(Guid id, CafeteriaServiceEventKind kind, DateTimeOffset at, T payload) => new()
    {
        OperationId = id,
        Kind = (int)kind,
        CustomerId = 7,
        OccurredAt = at,
        CreatedAt = at,
        PayloadJson = JsonSerializer.Serialize(payload)
    };

    private sealed class Factory(DbContextOptions<ServiceDbContext> options) : IDbContextFactory<ServiceDbContext>
    {
        public ServiceDbContext CreateDbContext() => new(options);
    }
}
