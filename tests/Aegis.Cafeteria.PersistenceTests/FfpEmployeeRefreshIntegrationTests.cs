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
    public async Task Refresh_CachesIdentityEligibilityWithoutLegacyTicketFunding()
    {
        var file=SqliteTestDatabase.CreatePath("ffp-refresh");
        try
        {
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}"}));
            await using(var db=factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var cache=new EfFfpReferenceCache(factory);
            var health=new EfCustomerReferenceCacheStatus(factory);
            var source=new StubSource([new ExternalCustomerSnapshot(101,"Jane Doe",true,"nfc","code","trn",[1,2,3])]);
            var service=new ReferenceDataRefreshService(source,cache,health,NullLogger<ReferenceDataRefreshService>.Instance);

            Assert.Equal(1,await service.RefreshCustomersAsync());

            await using var verify=factory.CreateDbContext();
            var customer=Assert.Single(await verify.Customers.ToListAsync());
            Assert.True(customer.IsEligibleForCredit);
            Assert.Equal("Jane Doe",customer.Name);
            Assert.Null(verify.Model.FindEntityType("Aegis.Cafeteria.Infrastructure.Persistence.Models.CustomerFundingProfileRow"));
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public void ExternalCustomerSnapshot_HasNoLegacyTicketOrFfpBalanceProperties()
    {
        var names=typeof(ExternalCustomerSnapshot).GetProperties().Select(x=>x.Name).ToHashSet();
        foreach(var forbidden in new[]{"TicketsAssigned","TicketsRemaining","MonthlyCreditLimit","SourceAvailableBalance","CreditLimit","AvailableBalance"})
            Assert.DoesNotContain(forbidden,names);
    }

    private sealed class StubSource(IReadOnlyList<ExternalCustomerSnapshot> customers):IWorkforceReferenceSource
    {
        public Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken=default)=>Task.FromResult(customers);
        public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyCollection<DateOnly>>([]);
    }
}
