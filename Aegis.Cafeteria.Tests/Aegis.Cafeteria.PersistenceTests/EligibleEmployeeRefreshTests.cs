namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class EligibleEmployeeRefreshTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Refresh_OnlyLoadsEligibleEmployees_AndRetiresOldRoster(bool paged)
    {
        string file = SqliteTestDatabase.CreatePath("eligible-roster");
        try
        {
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Data Source={file}" }));
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            EfWorkforceReferenceCache cache = new(factory);
            await cache.ReplaceCustomersAsync([new() { CustomerId = 1, Name = "Former Employee", IsEligibleForCafeteriaCredit = true, Code = "OLD" }]);
            ExternalCustomerSnapshot[] employees =
            [
                new() { CustomerId = 1, Name = "Former Employee", IsActive = false, IsEligibleForCafeteriaCredit = true, Code = "OLD" },
                new() { CustomerId = 2, Name = "Ineligible", Code = "NO" },
                new() { CustomerId = 3, Name = "Eligible", IsEligibleForCafeteriaCredit = true, Code = "YES" }
            ];
            IWorkforceReferenceSource source = paged ? new PagedSource(employees) : new Source(employees);
            EfCustomerReferenceCacheStatus status = new(factory);
            ReferenceDataRefreshService refresh = new(source, cache, status, NullLogger<ReferenceDataRefreshService>.Instance);

            Assert.Equal(1, await refresh.RefreshCustomersAsync());
            Assert.Equal(1, (await status.GetAsync()).EmployeeCount);
            await using LocalCafeteriaDbContext final = factory.CreateDbContext();
            Assert.False((await final.Customers.SingleAsync(customer => customer.Id == 1)).IsEligibleForCredit);
            Assert.False((await final.Customers.SingleAsync(customer => customer.Id == 1)).IsActive);
            Assert.False(await final.Customers.AnyAsync(customer => customer.Id == 2));
            Assert.Equal(3, (await final.CustomerIdentifiers.SingleAsync()).CustomerId);
            Assert.True((await final.Customers.SingleAsync(customer => customer.Id == 3)).IsEligibleForCredit);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    private class Source(ExternalCustomerSnapshot[] employees) : IWorkforceReferenceSource
    {
        protected ExternalCustomerSnapshot[] Employees { get; } = employees;
        public Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExternalCustomerSnapshot>>(Employees);
        public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<DateOnly>>([]);
    }

    private sealed class PagedSource(ExternalCustomerSnapshot[] employees) : Source(employees), IPagedWorkforceReferenceSource
    {
        public Task<CustomerReferenceBatch> GetCustomerBatchAsync(int? afterCustomerId, int take, CancellationToken cancellationToken = default)
            => Task.FromResult(new CustomerReferenceBatch(Employees, null));
    }
}
