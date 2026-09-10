namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class FfpEmployeeRefreshIntegrationTests
{
    [Fact]
    public async Task Refresh_CachesIdentityEligibilityPhotoAndEmploymentMetadataWithoutLegacyTicketFunding()
    {
        string file = SqliteTestDatabase.CreatePath("ffp-refresh");
        try
        {
            CafeteriaPersistenceOptions options = new() { LocalSqliteConnectionString = $"Data Source={file}" };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();

            byte[] photo = [1, 2, 3];
            DateOnly startDate = new(2026, 9, 14);
            EfFfpReferenceCache cache = new(factory);
            EfCustomerReferenceCacheStatus health = new(factory);
            StubSource source = new
            (
                [
                    new ExternalCustomerSnapshot
                    {
                          CustomerId = 101
                        , Name = "Jane Doe"
                        , IsActive = true
                        , EmploymentStartDate = startDate
                        , IsEligibleForCafeteriaCredit = true
                        , Nfc = "nfc"
                        , Code = "code"
                        , Trn = "trn"
                        , Photo = photo
                    }
                ]
            );
            ReferenceDataRefreshService service = new(source, cache, health, NullLogger<ReferenceDataRefreshService>.Instance);

            Assert.Equal(1, await service.RefreshCustomersAsync());

            await using LocalCafeteriaDbContext verify = factory.CreateDbContext();
            var customer = Assert.Single(await verify.Customers.ToListAsync());
            Assert.True(customer.IsActive);
            Assert.True(customer.IsEligibleForCredit);
            Assert.Equal(startDate, customer.EmploymentStartDate);
            Assert.Equal("Jane Doe", customer.Name);
            Assert.Equal(photo, customer.Photo);
            Assert.Null(verify.Model.FindEntityType("Aegis.Cafeteria.Infrastructure.Persistence.Models.CustomerFundingProfileRow"));
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task Refresh_InactiveEmployee_IsNotEligibleForCredit()
    {
        string file = SqliteTestDatabase.CreatePath("ffp-inactive-refresh");
        try
        {
            CafeteriaPersistenceOptions options = new() { LocalSqliteConnectionString = $"Data Source={file}" };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();

            EfFfpReferenceCache cache = new(factory);
            EfCustomerReferenceCacheStatus health = new(factory);
            StubSource source = new
            (
                [
                    new ExternalCustomerSnapshot
                    {
                          CustomerId = 202
                        , Name = "Inactive Employee"
                        , IsActive = false
                        , IsEligibleForCafeteriaCredit = true
                    }
                ]
            );
            ReferenceDataRefreshService service = new(source, cache, health, NullLogger<ReferenceDataRefreshService>.Instance);

            await service.RefreshCustomersAsync();

            await using LocalCafeteriaDbContext verify = factory.CreateDbContext();
            var customer = Assert.Single(await verify.Customers.ToListAsync());
            Assert.False(customer.IsActive);
            Assert.False(customer.IsEligibleForCredit);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public void ExternalCustomerSnapshot_HasNoLegacyTicketOrFfpBalanceProperties()
    {
        HashSet<string> names = typeof(ExternalCustomerSnapshot).GetProperties().Select(x => x.Name).ToHashSet();
        foreach (string forbidden in new[] { "TicketsAssigned", "TicketsRemaining", "MonthlyCreditLimit", "SourceAvailableBalance", "CreditLimit", "AvailableBalance" })
        {
            Assert.DoesNotContain(forbidden, names);
        }
    }

    private sealed class StubSource(IReadOnlyList<ExternalCustomerSnapshot> customers) : IWorkforceReferenceSource
    {
        public Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default) => Task.FromResult(customers);

        public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<DateOnly>>([]);
    }
}
