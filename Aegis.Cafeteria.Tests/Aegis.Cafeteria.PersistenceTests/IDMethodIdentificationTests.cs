namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class IDMethodIdentificationTests
{
    [Theory]
    [InlineData(CustomerIdentifierType.NfcTag, "04-aa-bb-cc", IDMethod.NFC)]
    [InlineData(CustomerIdentifierType.Code, "EMP00042", IDMethod.Code)]
    [InlineData(CustomerIdentifierType.Trn, "123456789", IDMethod.TRN)]
    public async Task ExactIdentifier_CapturesMethodAndLiteralSearchValue(
        CustomerIdentifierType identifierType,
        string searchValue,
        IDMethod expectedMethod)
    {
        string file = SqliteTestDatabase.CreatePath("idmethod-identify");
        try
        {
            LocalDbContextFactory factory = await CreateCustomerAsync(file);
            EfCafeteriaPersistenceStore store = new(
                new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            string storedValue = identifierType switch
            {
                CustomerIdentifierType.NfcTag => "04-AA-BB-CC",
                CustomerIdentifierType.Code => "EMP00042",
                CustomerIdentifierType.Trn => "123-456-789",
                _ => throw new ArgumentOutOfRangeException(nameof(identifierType))
            };
            await store.SaveCustomerIdentifierAsync(new CustomerIdentifier(Guid.NewGuid(), 42, identifierType, storedValue));
            EfCustomerIdentificationService service = new(factory, new StubAccountQuery());

            CustomerAccountView account = Assert.IsType<CustomerAccountView>(await service.IdentifyAsync(identifierType, searchValue));

            Assert.NotNull(account.Identification);
            Assert.Equal(expectedMethod, account.Identification!.Method);
            Assert.Equal(searchValue, account.Identification.Code);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task NameSelection_CapturesNameAndNameSearchExactlyOnce()
    {
        string file = SqliteTestDatabase.CreatePath("idmethod-name");
        try
        {
            LocalDbContextFactory factory = await CreateCustomerAsync(file);
            EfCustomerIdentificationService service = new(factory, new StubAccountQuery());

            Assert.Single(await service.SearchAsync("Wright"));
            CustomerAccountView first = Assert.IsType<CustomerAccountView>(await service.LoadAsync(42));
            CustomerAccountView second = Assert.IsType<CustomerAccountView>(await service.LoadAsync(42));

            Assert.NotNull(first.Identification);
            Assert.Equal(IDMethod.Name, first.Identification!.Method);
            Assert.Equal("NAME SEARCH", first.Identification.Code);
            Assert.Null(second.Identification);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task ExactIdentifier_SupersedesPendingNameSearchContext()
    {
        string file = SqliteTestDatabase.CreatePath("idmethod-supersede");
        try
        {
            LocalDbContextFactory factory = await CreateCustomerAsync(file);
            EfCafeteriaPersistenceStore store = new(
                new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerIdentifierAsync(new CustomerIdentifier(Guid.NewGuid(), 42, CustomerIdentifierType.Code, "EMP00042"));
            EfCustomerIdentificationService service = new(factory, new StubAccountQuery());

            Assert.Single(await service.SearchAsync("Wright"));
            CustomerAccountView exact = Assert.IsType<CustomerAccountView>(await service.IdentifyAsync(CustomerIdentifierType.Code, "EMP00042"));
            CustomerAccountView laterLoad = Assert.IsType<CustomerAccountView>(await service.LoadAsync(42));

            Assert.Equal(IDMethod.Code, exact.Identification!.Method);
            Assert.Equal("EMP00042", exact.Identification.Code);
            Assert.Null(laterLoad.Identification);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    private static async Task<LocalDbContextFactory> CreateCustomerAsync(string file)
    {
        CafeteriaPersistenceOptions options = new()
        {
            LocalSqliteConnectionString = $"Data Source={file}",
            TerminalId = "TEST"
        };
        LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
        await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
        {
            await db.Database.EnsureCreatedAsync();
        }
        EfCafeteriaPersistenceStore store = new(
            new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
        await store.SaveCustomerAsync(new Customer(42, "Fitzroy Wright", null, true, 0m));
        return factory;
    }

    private sealed class StubAccountQuery : ICustomerAccountQuery
    {
        public Task<CustomerAccountView?> GetAsync(
            int customerId,
            int year,
            int month,
            CancellationToken cancellationToken = default)
            => Task.FromResult<CustomerAccountView?>(new CustomerAccountView(
                new CustomerIdentityView(customerId, "Fitzroy Wright", null, true),
                new AccountPeriodView(year, month, 0, 0m, 0m, false),
                new CustomerFinancialStatusView(0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m),
                [],
                new ExternalPublicationStatus(false, 0, 0, null, null)));

        public Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(
            int customerId,
            int year,
            int month,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerMonthlyTransactionView>>([]);
    }
}
