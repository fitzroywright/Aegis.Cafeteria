namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class IDMethodV73UpgradeTests
{
    [Fact]
    public async Task ExistingV73Database_AddsIDMethodColumnsWithoutRewritingFinancialRows()
    {
        string file = SqliteTestDatabase.CreatePath("cafeteria-idmethod-v73-upgrade");
        try
        {
            CafeteriaPersistenceOptions options = new()
            {
                LocalSqliteConnectionString = $"Data Source={file}",
                TerminalId = "TEST"
            };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            Guid orderId = Guid.NewGuid();
            Guid operationId = Guid.NewGuid();
            DateTimeOffset at = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.LocalSchemaInfo.Add(new LocalSchemaInfoRow
                {
                    Id = 1,
                    SchemaVersion = PersistenceInitializer.CurrentLocalSchemaVersion,
                    ProductVersion = PersistenceInitializer.CurrentProductVersion,
                    CreatedAt = at
                });
                db.Customers.Add(new CustomerRow
                {
                    Id = 42,
                    Name = "Legacy Employee",
                    NormalizedName = "LEGACY EMPLOYEE",
                    Department = "IT",
                    IsEligibleForCredit = true,
                    Revision = 1,
                    UpdatedAt = at
                });
                db.Orders.Add(new OrderRow
                {
                    Id = orderId,
                    CustomerId = 42,
                    CreatedAt = at.AddMinutes(-1),
                    Status = 2,
                    SubmittedAt = at.AddSeconds(-30),
                    CompletedAt = at,
                    AccountYear = 2026,
                    AccountMonth = 9,
                    CashierUserId = "legacy.cashier",
                    TerminalId = "POS01",
                    Revision = 7,
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
                            Method = 1,
                            Amount = 500m,
                            RecordedAt = at
                        }
                    ]
                });
                db.FfpLedgerOutbox.Add(new FfpLedgerOutboxRow
                {
                    OperationId = operationId,
                    EmployeeId = 42,
                    Kind = 2,
                    OccurredAt = at,
                    Description = "Legacy Purchase",
                    ScanValue = 500m,
                    CreditLimit = 5000m,
                    AvailableBalance = 4500m,
                    ScanCode = "PURCHASE",
                    UserName = "legacy.cashier",
                    IdName = "Cafeteria Purchase",
                    State = FfpOutboxState.Pending,
                    CreatedAt = at
                });
                await db.SaveChangesAsync();

                await db.Database.ExecuteSqlRawAsync("DROP INDEX IF EXISTS \"IX_Orders_IDMethod_CompletedAt\";");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Orders\" DROP COLUMN \"IDMethod\";");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Orders\" DROP COLUMN \"IdentificationCode\";");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"Orders\" DROP COLUMN \"CashierName\";");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FfpLedgerOutbox\" DROP COLUMN \"Code\";");
                await db.Database.ExecuteSqlRawAsync("ALTER TABLE \"FfpLedgerOutbox\" DROP COLUMN \"CreatedBy\";");
            }

            PersistenceInitializer initializer = new(factory, NullLogger<PersistenceInitializer>.Instance);
            await initializer.InitializeLocalAsync();

            await using LocalCafeteriaDbContext verify = factory.CreateDbContext();
            OrderRow order = await verify.Orders.AsNoTracking().SingleAsync(x => x.Id == orderId);
            Assert.Equal(42, order.CustomerId);
            Assert.Equal(at, order.CompletedAt);
            Assert.Equal("legacy.cashier", order.CashierUserId);
            Assert.Equal(7, order.Revision);
            Assert.Null(order.IDMethod);
            Assert.Null(order.IdentificationCode);
            Assert.Null(order.CashierName);

            FfpLedgerOutboxRow outbox = await verify.FfpLedgerOutbox.AsNoTracking().SingleAsync(x => x.OperationId == operationId);
            Assert.Equal(500m, outbox.ScanValue);
            Assert.Equal(4500m, outbox.AvailableBalance);
            Assert.Equal("PURCHASE", outbox.ScanCode);
            Assert.Null(outbox.Code);
            Assert.Equal("SYSTEM", outbox.CreatedBy);

            LocalSchemaInfoRow schema = await verify.LocalSchemaInfo.AsNoTracking().SingleAsync(x => x.Id == 1);
            Assert.Equal(73, schema.SchemaVersion);
            Assert.Equal("v73", schema.ProductVersion);
        }
        finally
        {
            SqliteTestDatabase.Delete(file);
        }
    }
}
