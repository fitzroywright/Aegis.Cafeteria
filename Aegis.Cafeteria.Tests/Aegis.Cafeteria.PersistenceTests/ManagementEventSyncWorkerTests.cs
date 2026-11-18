using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

using Xunit;

namespace Aegis.Cafeteria.PersistenceTests;

public sealed class ManagementEventSyncWorkerTests
{
    [Fact]
    public async Task ReservedProductImport_IsAcknowledgedWithoutCreatingAProduct()
    {
        string file = SqliteTestDatabase.CreatePath("product-import-sync");
        try
        {
            var factory = new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(
                new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Data Source={file}" }));
            await using var db = factory.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            var reserved = ProductEvent(1, " \tfFp-JUICE ", "Policy Juice");
            var saleable = ProductEvent(2, "JUICE", "Juice");
            using var http = new HttpClient(new Feed(reserved, saleable)) { BaseAddress = new Uri("http://test/") };
            var worker = new ManagementEventSyncWorker(new CafeteriaServicesClient(http), new(), factory, null!, null!,
                new CustomerAccountChangeNotifier(NullLogger<CustomerAccountChangeNotifier>.Instance),
                new ManagementEventWakeSignal(), NullLogger<ManagementEventSyncWorker>.Instance);

            Assert.Equal(2, await worker.PullAndApplyAsync(default));
            Assert.Equal("Juice", Assert.Single(await new EfProductCatalogQuery(factory).GetAllAsync()).Name);
            var inbox = await db.ManagementEventInbox.SingleAsync(row => row.OperationId == reserved.OperationId);
            Assert.NotNull(inbox.ProcessedAt);
            Assert.Null(inbox.LastError);
            Assert.Equal(reserved.PayloadJson, inbox.PayloadJson);
            Assert.Equal(0, await worker.PullAndApplyAsync(default));
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }

        static CafeteriaServiceEventEnvelope ProductEvent(long sequence, string code, string name)
        {
            Guid operation = Guid.NewGuid();
            DateTimeOffset now = DateTimeOffset.UtcNow;
            ProductCatalogImportServiceEvent import = new(operation, (int)sequence, code, name, 200m, "Manager", now);
            return new(sequence, operation, CafeteriaServiceEventKind.ProductCatalogImport, 0, now, JsonSerializer.Serialize(import));
        }
    }

    [Fact]
    public async Task PosTransactionAcknowledgement_DoesNotBlockMonthlyAllocation_AndReplayIsIdempotent()
    {
        SQLitePCL.Batteries_V2.Init();

        string file = Path.Combine(Path.GetTempPath(), $"month-sync-{Guid.NewGuid():N}.db");
        try
        {
            var factory = new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(
                new CafeteriaPersistenceOptions { LocalSqliteConnectionString = $"Data Source={file}", TerminalId = "SYNC-TEST" }));
            await using (var db = factory.CreateDbContext()) await db.Database.EnsureCreatedAsync();
            var persistence = new EfCafeteriaPersistenceStore(new LocalPersistenceStore(factory, NullLogger<LocalPersistenceStore>.Instance));
            await persistence.SaveCustomerAsync(new Customer(7, "Sync Test Employee", null, true, 0m));
            var options = new CafeteriaServicesOptions { Enabled = true };
            var query = new EfCustomerAccountQuery(factory,
                new MonthlyCreditAllocationService(new Days(), new EfCafeteriaRatePolicyProvider(factory), persistence,
                    executionPolicy: new MonthlyAllocationExecutionPolicy(options)),
                new EfExternalPublicationStatusProvider(factory, options));
            var before = await query.GetAsync(7, 2026, 10);
            Assert.NotNull(before);
            Assert.Equal(0m, before.FinancialStatus.CreditLimit);
            var allocation = new MonthlyAllocationServiceEvent(Guid.NewGuid(), 7, 2026, 10, 20, true, 800m, 600m,
                DateTimeOffset.UtcNow, "Sync Test Employee");
            var echoedPos = new CafeteriaServiceEventEnvelope(1, Guid.NewGuid(), CafeteriaServiceEventKind.PosTransaction, 7,
                DateTimeOffset.UtcNow, "{}");
            var allocationEvent = new CafeteriaServiceEventEnvelope(2, allocation.OperationId, CafeteriaServiceEventKind.MonthlyAllocation,
                7, allocation.CalculatedAt, JsonSerializer.Serialize(allocation));
            using var http = new HttpClient(new Feed(echoedPos, allocationEvent)) { BaseAddress = new Uri("http://test/") };
            var notifier = new CustomerAccountChangeNotifier(NullLogger<CustomerAccountChangeNotifier>.Instance);
            int notifications = 0;
            using var subscription = notifier.Subscribe(_ => { notifications++; return Task.CompletedTask; });
            var worker = new ManagementEventSyncWorker(new CafeteriaServicesClient(http), options, factory, persistence, query,
                notifier, new ManagementEventWakeSignal(), NullLogger<ManagementEventSyncWorker>.Instance);
            Assert.Equal(2, await worker.PullAndApplyAsync(default));
            var after = await query.GetAsync(7, 2026, 10);
            Assert.NotNull(after);
            Assert.Equal(16000m, after.FinancialStatus.CreditLimit);
            Assert.Equal(16000m, after.FinancialStatus.TotalAvailableToSpend);
            Assert.Equal(0, await worker.PullAndApplyAsync(default));
            await using (var db = factory.CreateDbContext())
            {
                Assert.Equal(1, await db.MonthlyCreditAllocations.CountAsync());
                Assert.Equal(2, await db.ManagementEventInbox.CountAsync(x => x.ProcessedAt != null));
                Assert.Equal(0, await db.Orders.CountAsync());
            }
            Assert.Equal(2, notifications);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(file + suffix);
        }
    }

    private sealed class Days : IWorkingDayProvider
    {
        public Task<int> GetWorkingDaysAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult(20);
    }

    private sealed class Feed(params CafeteriaServiceEventEnvelope[] events) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            long after = long.Parse(request.RequestUri!.Query.TrimStart('?').Split('&')[0].Split('=')[1]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(events.Where(x => x.Sequence > after).ToArray())
            });
        }
    }
}
