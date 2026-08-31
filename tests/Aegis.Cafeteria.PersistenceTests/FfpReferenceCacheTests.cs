namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class FfpReferenceCacheTests
{
    [Fact]
    public async Task EmployeeRefresh_ReplacesIdentifiersAndEligibility_WithoutLegacyFundingState()
    {
        var file = SqliteTestDatabase.CreatePath("ffp-cache");
        try
        {
            var options = new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Data Source={file}" };
            var factory = new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using (var create = factory.CreateDbContext()) await create.Database.EnsureCreatedAsync();

            var cache = new EfFfpReferenceCache(factory);
            await cache.ReplaceCustomersAsync([
                new ExternalCustomerSnapshot(101, "Jane Doe", true, "NFC-A", "BC-A", "123456789", null)
            ]);
            await cache.ReplaceCustomersAsync([
                new ExternalCustomerSnapshot(101, "Jane Doe", false, "NFC-B", "BC-B", "123456789", null, "", 5000m)
            ]);

            await using var db = await factory.CreateDbContextAsync();
            var employee = await db.Customers.SingleAsync(x => x.Id == 101);
            Assert.False(employee.IsEligibleForCredit);
            Assert.Equal(5000m, employee.AdditionalCreditLimit);

            var ids = await db.CustomerIdentifiers.Where(x => x.CustomerId == 101).ToListAsync();
            Assert.DoesNotContain(ids, x => x.Value == "nfc-a");
            Assert.Contains(ids, x => x.NormalizedValue == "nfc-b" && x.Type == (int)CustomerIdentifierType.NfcTag);
            Assert.Contains(ids, x => x.NormalizedValue == "bc-b" && x.Type == (int)CustomerIdentifierType.Code);
            Assert.Contains(ids, x => x.NormalizedValue == "123456789" && x.Type == (int)CustomerIdentifierType.Trn);

            Assert.Null(db.Model.FindEntityType("Aegis.Cafeteria.Infrastructure.Persistence.Models.CustomerFundingProfileRow"));
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public void ExternalCustomerSnapshot_DoesNotExposeTicketOrFfpBalanceConcepts()
    {
        var names = typeof(ExternalCustomerSnapshot).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var forbidden in new[] { "TicketsAssigned", "TicketsRemaining", "CreditLimit", "AvailableBalance", "MonthlyCreditLimit", "SourceAvailableBalance" })
            Assert.DoesNotContain(forbidden, names);
    }
}
