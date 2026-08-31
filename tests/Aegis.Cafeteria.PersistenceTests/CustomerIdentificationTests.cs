namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class CustomerIdentificationTests
{
    [Fact]
    public async Task NfcCodeAndTrn_ResolveFromLocalIndexedData()
    {
        var file=SqliteTestDatabase.CreatePath("cafeteria-id");
        try
        {
            var options=new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}",TerminalId="TEST"};
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using(var db=factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var store=new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(42,"Fitzroy Wright",null,true,5_000m));
            await store.SaveCustomerIdentifierAsync(new CustomerIdentifier(Guid.NewGuid(),42,CustomerIdentifierType.NfcTag,"04-AA-BB-CC"));
            await store.SaveCustomerIdentifierAsync(new CustomerIdentifier(Guid.NewGuid(),42,CustomerIdentifierType.Code,"EMP00042"));
            await store.SaveCustomerIdentifierAsync(new CustomerIdentifier(Guid.NewGuid(),42,CustomerIdentifierType.Trn,"123-456-789"));
            var service=new EfCustomerIdentificationService(factory,new StubAccountQuery());
            Assert.Equal(42,(await service.IdentifyAsync(CustomerIdentifierType.NfcTag,"04-aa-bb-cc"))!.Customer.Id);
            Assert.Equal(42,(await service.IdentifyAsync(CustomerIdentifierType.Code,"emp00042"))!.Customer.Id);
            Assert.Equal(42,(await service.IdentifyAsync(CustomerIdentifierType.Trn,"123456789"))!.Customer.Id);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public async Task TrnLookup_WhenNormalizationIsEmpty_DoesNotQueryOrReturnEmptyIdentifierRow()
    {
        var file=SqliteTestDatabase.CreatePath("cafeteria-empty-trn");
        try
        {
            var options=new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}",TerminalId="TEST"};
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using(var db=factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.Customers.Add(new Aegis.Cafeteria.Infrastructure.Persistence.Models.CustomerRow
                { Id=99, Name="Wrong Person", NormalizedName="WRONG PERSON", IsEligibleForCredit=true, Revision=1, UpdatedAt=DateTimeOffset.UtcNow });
                db.CustomerIdentifiers.Add(new Aegis.Cafeteria.Infrastructure.Persistence.Models.CustomerIdentifierRow
                { Id=Guid.NewGuid(), CustomerId=99, Type=(int)CustomerIdentifierType.Trn, Value="", NormalizedValue="", Revision=1, UpdatedAt=DateTimeOffset.UtcNow });
                await db.SaveChangesAsync();
            }

            var service=new EfCustomerIdentificationService(factory,new StubAccountQuery());
            var result=await service.IdentifyAsync(CustomerIdentifierType.Trn,"Wright");
            Assert.Null(result);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public async Task Search_ReturnsEmployeeDirectoryDetailsFromLocalCache()
    {
        var file=SqliteTestDatabase.CreatePath("cafeteria-search");
        try
        {
            var options=new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}",TerminalId="TEST"};
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using(var db=factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var store=new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(7,"Jane Brown",new byte[1000],true,1_000m));
            await store.SaveCustomerIdentifierAsync(new CustomerIdentifier(Guid.NewGuid(),7,CustomerIdentifierType.Trn,"111-222-333"));
            var service=new EfCustomerIdentificationService(factory,new StubAccountQuery());
            var result=await service.SearchAsync("Jan");
            var item=Assert.Single(result);
            Assert.Equal(7,item.CustomerId);
            Assert.Equal("Jane Brown",item.Name);
            Assert.True(item.IsEligibleForCredit);
            Assert.NotNull(item.Photo);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }


    [Fact]
    public async Task Search_NameMatchesAnywhereLikeProductSearch()
    {
        var file=SqliteTestDatabase.CreatePath("cafeteria-name-contains");
        try
        {
            var options=new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}",TerminalId="TEST"};
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using(var db=factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var store=new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(7,"Fitzroy Wright",null,true,0m));
            await store.SaveCustomerAsync(new Customer(8,"Amy Brown",null,true,0m));
            var service=new EfCustomerIdentificationService(factory,new StubAccountQuery());

            var result=await service.SearchAsync("Wright");

            var item=Assert.Single(result);
            Assert.Equal(7,item.CustomerId);
            Assert.Equal("Fitzroy Wright",item.Name);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public async Task Search_DifferentPartialNames_ReturnDifferentCustomers()
    {
        var file=SqliteTestDatabase.CreatePath("cafeteria-distinct-name-search");
        try
        {
            var options=new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}",TerminalId="TEST"};
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using(var db=factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var store=new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(7,"Fitzroy Wright",null,true,0m));
            await store.SaveCustomerAsync(new Customer(8,"Amy Brown",null,true,0m));
            await store.SaveCustomerAsync(new Customer(9,"Marcus Campbell",null,true,0m));
            var service=new EfCustomerIdentificationService(factory,new StubAccountQuery());

            var wright=Assert.Single(await service.SearchAsync("Wri"));
            var brown=Assert.Single(await service.SearchAsync("Bro"));
            var campbell=Assert.Single(await service.SearchAsync("Camp"));

            Assert.Equal(7,wright.CustomerId);
            Assert.Equal(8,brown.CustomerId);
            Assert.Equal(9,campbell.CustomerId);
            Assert.NotEqual(wright.CustomerId,brown.CustomerId);
            Assert.NotEqual(brown.CustomerId,campbell.CustomerId);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    [Fact]
    public async Task EmptySearch_BrowsesEmployeesAlphabetically()
    {
        var file=SqliteTestDatabase.CreatePath("cafeteria-browse");
        try
        {
            var options=new CafeteriaPersistenceOptions{LocalSqliteConnectionString=$"Data Source={file}",TerminalId="TEST"};
            var factory=new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
            await using(var db=factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var store=new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await store.SaveCustomerAsync(new Customer(2,"Zoe King",null,false,0m));
            await store.SaveCustomerAsync(new Customer(1,"Amy Brown",null,true,0m));
            var service=new EfCustomerIdentificationService(factory,new StubAccountQuery());

            var result=await service.SearchAsync(string.Empty,50);

            Assert.Equal(2,result.Count);
            Assert.Equal("Amy Brown",result[0].Name);
            Assert.True(result[0].IsEligibleForCredit);
            Assert.Equal("Zoe King",result[1].Name);
            Assert.False(result[1].IsEligibleForCredit);
        }
        finally { SqliteTestDatabase.Delete(file); }
    }

    private sealed class StubAccountQuery:ICustomerAccountQuery
    {
        public Task<CustomerAccountView?> GetAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)
            => Task.FromResult<CustomerAccountView?>(new(
                new CustomerIdentityView(customerId,"Customer",null,true),
                new AccountPeriodView(year,month,0,0,0,false),
                new CustomerFinancialStatusView(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0),
                [],
                new FfpPublicationStatus(false,0,0,null,null)));

        public Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(
            int customerId, int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerMonthlyTransactionView>>([]);
    }
}
