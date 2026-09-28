namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class WorkforceReferenceCacheTests
{
    [Fact]
    public async Task Batches_KeepUnseenEmployeesUntilCompletion_AndPreserveFirstIdentifierClaim()
    {
        string file = SqliteTestDatabase.CreatePath("workforce-batches");
        try
        {
            CafeteriaPersistenceOptions options = new() { LocalSqliteConnectionString = $"Data Source={file}" };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await using (LocalCafeteriaDbContext create = factory.CreateDbContext()) await create.Database.EnsureCreatedAsync();
            EfWorkforceReferenceCache cache = new(factory);
            await cache.ReplaceCustomersAsync
            (
                [new() { CustomerId = 1, Name = "Old", Code = "OLD" }, new() { CustomerId = 20, Name = "Cached", Code = "CACHED" }]
            );
            await cache.UpsertCustomerBatchAsync([new() { CustomerId = 1, Name = "Updated", Code = "SHARED" }], new HashSet<int>());
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                Assert.True((await db.Customers.SingleAsync(customer => customer.Id == 20)).IsActive);
                Assert.True(await db.CustomerIdentifiers.AnyAsync(identifier => identifier.CustomerId == 20));
                Assert.Equal("Updated", (await db.Customers.SingleAsync(customer => customer.Id == 1)).Name);
            }
            // Saving the same batch again after an interrupted refresh is safe.
            await cache.UpsertCustomerBatchAsync([new() { CustomerId = 1, Name = "Updated", Code = "SHARED" }], new HashSet<int>());
            await cache.UpsertCustomerBatchAsync([new() { CustomerId = 7, Name = "Next", Code = "SHARED" }], new HashSet<int> { 1 });
            await cache.CompleteCustomerRefreshAsync(new HashSet<int> { 1, 7 });
            await using LocalCafeteriaDbContext final = factory.CreateDbContext();
            Assert.False((await final.Customers.SingleAsync(customer => customer.Id == 20)).IsActive);
            Assert.False(await final.CustomerIdentifiers.AnyAsync(identifier => identifier.CustomerId == 20));
            Assert.Equal(1, (await final.CustomerIdentifiers.SingleAsync(identifier => identifier.NormalizedValue == "SHARED")).CustomerId);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public async Task EmployeeRefresh_ReplacesIdentifiersAndEligibility_WithoutLegacyFundingState()
    {
        string file = SqliteTestDatabase.CreatePath("workforce-cache");
        try
        {
            CafeteriaPersistenceOptions options = new() { LocalSqliteConnectionString = $"Data Source={file}" };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await using (LocalCafeteriaDbContext create = factory.CreateDbContext()) await create.Database.EnsureCreatedAsync();

            EfWorkforceReferenceCache cache = new(factory);
            await cache.ReplaceCustomersAsync
            (
                [new ExternalCustomerSnapshot { CustomerId = 101, Name = "Jane Doe", IsEligibleForCafeteriaCredit = true, Nfc = "NFC-A", Code = "BC-A", Trn = "123456789" }]
            );
            await cache.ReplaceCustomersAsync
            (
                [new ExternalCustomerSnapshot { CustomerId = 101, Name = "Jane Doe", IsEligibleForCafeteriaCredit = false, Nfc = "NFC-B", Code = "BC-B", Trn = "123456789", AdditionalCreditLimit = 5000m }]
            );

            await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync();
            var employee = await db.Customers.SingleAsync(x => x.Id == 101);
            Assert.False(employee.IsEligibleForCredit);
            Assert.Equal(5000m, employee.AdditionalCreditLimit);

            var ids = await db.CustomerIdentifiers.Where(x => x.CustomerId == 101).ToListAsync();
            Assert.DoesNotContain(ids, x => x.NormalizedValue == "NFC-A");
            Assert.Contains(ids, x => x.NormalizedValue == "NFC-B" && x.Type == (int)CustomerIdentifierType.NfcTag);
            Assert.Contains(ids, x => x.NormalizedValue == "BC-B" && x.Type == (int)CustomerIdentifierType.Code);
            Assert.Contains(ids, x => x.NormalizedValue == "123456789" && x.Type == (int)CustomerIdentifierType.Trn);

            Assert.Null(db.Model.FindEntityType("Aegis.Cafeteria.Infrastructure.Persistence.Models.CustomerFundingProfileRow"));
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public void ExternalCustomerSnapshot_DoesNotExposeLegacyBalanceConcepts()
    {
        HashSet<string> names = typeof(ExternalCustomerSnapshot).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (string forbidden in new[] { "TicketsAssigned", "TicketsRemaining", "CreditLimit", "AvailableBalance", "MonthlyCreditLimit", "SourceAvailableBalance" }) Assert.DoesNotContain(forbidden, names);
    }
}
