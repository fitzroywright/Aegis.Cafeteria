namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class IDMethodFfpRecoveryTests
{
    [Fact]
    public async Task FailedOutboxRetry_PreservesIDMethodMetadata()
    {
        string file = SqliteTestDatabase.CreatePath("cafeteria-idmethod-retry");
        try
        {
            LocalDbContextFactory factory = Factory(file);
            Guid operationId = Guid.NewGuid();
            DateTimeOffset at = DateTimeOffset.UtcNow;
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.FfpLedgerOutbox.Add(new FfpLedgerOutboxRow
                {
                    OperationId = operationId,
                    EmployeeId = 42,
                    Kind = (int)ExternalLedgerEntryKind.Purchase,
                    OccurredAt = at,
                    Description = "Lunch",
                    ScanValue = 500m,
                    CreditLimit = 5000m,
                    AvailableBalance = 4500m,
                    ScanCode = "TRN",
                    UserName = "Fitzroy Wright",
                    IdName = "Cafeteria Purchase",
                    Code = "123456789",
                    CreatedBy = "SYSTEM",
                    State = FfpOutboxState.Failed,
                    AttemptCount = 1,
                    CreatedAt = at
                });
                await db.SaveChangesAsync();
            }

            RecordingGateway gateway = new();
            FfpLedgerPublisherWorker worker = Worker(factory, gateway);
            Assert.Equal(1, await worker.PublishBatchAsync(CancellationToken.None));

            ExternalLedgerEntry entry = Assert.Single(gateway.Entries);
            Assert.Equal(operationId, entry.OperationId);
            Assert.Equal("TRN", entry.ScanCode);
            Assert.Equal("123456789", entry.Code);
            Assert.Equal("Fitzroy Wright", entry.UserName);
            Assert.Equal("SYSTEM", entry.CreatedBy);

            await using LocalCafeteriaDbContext verify = factory.CreateDbContext();
            FfpLedgerOutboxRow row = await verify.FfpLedgerOutbox.SingleAsync(x => x.OperationId == operationId);
            Assert.Equal(FfpOutboxState.Succeeded, row.State);
            Assert.Equal(2, row.AttemptCount);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Theory]
    [InlineData(IDMethod.NFC, "04A1B2C3", "NFC")]
    [InlineData(IDMethod.Code, "EMP0042", "Code")]
    [InlineData(IDMethod.TRN, "123456789", "TRN")]
    [InlineData(IDMethod.Name, "NAME SEARCH", "Name")]
    public async Task MissingPurchaseOutboxRecovery_ReconstructsIDMethodMetadata(
        IDMethod method,
        string identificationCode,
        string expectedScanCode)
    {
        string file = SqliteTestDatabase.CreatePath("cafeteria-idmethod-recover");
        try
        {
            LocalDbContextFactory factory = Factory(file);
            Guid orderId = Guid.NewGuid();
            DateTimeOffset at = DateTimeOffset.UtcNow;
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.Orders.Add(new OrderRow
                {
                    Id = orderId,
                    CustomerId = 42,
                    CreatedAt = at.AddMinutes(-1),
                    Status = (int)OrderStatus.Completed,
                    SubmittedAt = at.AddSeconds(-30),
                    CompletedAt = at,
                    AccountYear = at.Year,
                    AccountMonth = at.Month,
                    CashierUserId = "fwright",
                    CashierName = "Fitzroy Wright",
                    TerminalId = "POS01",
                    IDMethod = (int)method,
                    IdentificationCode = identificationCode,
                    Revision = 1,
                    UpdatedAt = at,
                    Items =
                    [
                        new OrderItemRow
                        {
                            Id = Guid.NewGuid(),
                            OrderId = orderId,
                            ProductId = 1,
                            ProductName = "Lunch",
                            UnitPrice = 500m,
                            Quantity = 1
                        }
                    ],
                    Payments =
                    [
                        new OrderPaymentRow
                        {
                            Id = Guid.NewGuid(),
                            OrderId = orderId,
                            Method = (int)PaymentMethod.CafeteriaAccount,
                            Amount = 500m,
                            RecordedAt = at
                        }
                    ]
                });
                await db.SaveChangesAsync();
            }

            FfpLedgerPublisherWorker worker = Worker(factory, new RecordingGateway());
            await worker.RecoverMissingProjectionRowsAsync(CancellationToken.None);

            await using LocalCafeteriaDbContext verify = factory.CreateDbContext();
            FfpLedgerOutboxRow row = await verify.FfpLedgerOutbox.SingleAsync(x => x.OperationId == orderId);
            Assert.Equal((int)ExternalLedgerEntryKind.Purchase, row.Kind);
            Assert.Equal(expectedScanCode, row.ScanCode);
            Assert.Equal(identificationCode, row.Code);
            Assert.Equal("Fitzroy Wright", row.UserName);
            Assert.Equal("SYSTEM", row.CreatedBy);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    [Fact]
    public async Task InterruptedPublicationRecovery_KeepsIDMethodMetadataForRetry()
    {
        string file = SqliteTestDatabase.CreatePath("cafeteria-idmethod-interrupted");
        try
        {
            LocalDbContextFactory factory = Factory(file);
            Guid operationId = Guid.NewGuid();
            DateTimeOffset at = DateTimeOffset.UtcNow;
            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.FfpLedgerOutbox.Add(new FfpLedgerOutboxRow
                {
                    OperationId = operationId,
                    EmployeeId = 42,
                    Kind = (int)ExternalLedgerEntryKind.Purchase,
                    OccurredAt = at,
                    Description = "Lunch",
                    ScanValue = 500m,
                    CreditLimit = 5000m,
                    AvailableBalance = 4500m,
                    ScanCode = "Name",
                    UserName = "Cashier Display Name",
                    IdName = "Cafeteria Purchase",
                    Code = "NAME SEARCH",
                    CreatedBy = "SYSTEM",
                    State = FfpOutboxState.Processing,
                    AttemptCount = 1,
                    CreatedAt = at
                });
                await db.SaveChangesAsync();
            }

            RecordingGateway gateway = new();
            FfpLedgerPublisherWorker worker = Worker(factory, gateway);
            await worker.RecoverInterruptedAsync(CancellationToken.None);
            Assert.Equal(1, await worker.PublishBatchAsync(CancellationToken.None));

            ExternalLedgerEntry entry = Assert.Single(gateway.Entries);
            Assert.Equal("Name", entry.ScanCode);
            Assert.Equal("NAME SEARCH", entry.Code);
            Assert.Equal("Cashier Display Name", entry.UserName);
            Assert.Equal("SYSTEM", entry.CreatedBy);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }

    private static LocalDbContextFactory Factory(string file)
    {
        CafeteriaPersistenceOptions options = new()
        {
            LocalSqliteConnectionString = $"Data Source={file}",
            TerminalId = "TEST"
        };
        return new LocalDbContextFactory(DbContextOptionsFactory.CreateLocal(options));
    }

    private static FfpLedgerPublisherWorker Worker(LocalDbContextFactory factory, IExternalAccountingGateway gateway)
        => new(
            factory,
            gateway,
            new NullAccountQuery(),
            new CafeteriaPersistenceOptions { FfpPublicationBatchSize = 100, FfpPoisonMessageAttemptLimit = 25 },
            new CafeteriaServicesOptions { Enabled = true, UseServiceMonthlyAllocations = true },
            NullLogger<FfpLedgerPublisherWorker>.Instance);

    private sealed class RecordingGateway : IExternalAccountingGateway
    {
        public List<ExternalLedgerEntry> Entries { get; } = [];

        public Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ExternalAccountSummary>>([]);
    }

    private sealed class NullAccountQuery : ICustomerAccountQuery
    {
        public Task<CustomerAccountView?> GetAsync(int customerId, int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult<CustomerAccountView?>(null);

        public Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(
            int customerId,
            int year,
            int month,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerMonthlyTransactionView>>([]);
    }
}
