namespace Aegis.Cafeteria.ReportingTests;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Aegis.Cafeteria.Reporting;
using Aegis.Cafeteria.Reporting.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

public sealed class IDMethodReportTests
{
    [Fact]
    public async Task PurchaseItemDetail_IncludesPersistedIDMethodColumn()
    {
        string file = Path.Combine(Path.GetTempPath(), $"cafeteria-idmethod-report-{Guid.NewGuid():N}.db");
        try
        {
            CafeteriaPersistenceOptions options = new()
            {
                LocalSqliteConnectionString = $"Data Source={file}",
                TerminalId = "TEST"
            };
            LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            Guid orderId = Guid.NewGuid();
            DateTimeOffset completedAt = new(2026, 9, 10, 12, 30, 0, TimeSpan.Zero);

            await using (LocalCafeteriaDbContext db = factory.CreateDbContext())
            {
                await db.Database.EnsureCreatedAsync();
                db.Customers.Add(new CustomerRow
                {
                    Id = 42,
                    Name = "Fitzroy Wright",
                    NormalizedName = "FITZROY WRIGHT",
                    Department = "IT",
                    IsEligibleForCredit = true,
                    Revision = 1,
                    UpdatedAt = completedAt
                });
                db.Orders.Add(new OrderRow
                {
                    Id = orderId,
                    CustomerId = 42,
                    CreatedAt = completedAt.AddMinutes(-1),
                    Status = (int)OrderStatus.Completed,
                    SubmittedAt = completedAt.AddSeconds(-30),
                    CompletedAt = completedAt,
                    AccountYear = 2026,
                    AccountMonth = 9,
                    CashierUserId = "fwright",
                    CashierName = "Fitzroy Wright",
                    TerminalId = "POS01",
                    IDMethod = (int)IDMethod.NFC,
                    IdentificationCode = "04A1B2C3",
                    Revision = 1,
                    UpdatedAt = completedAt,
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
                            RecordedAt = completedAt
                        }
                    ]
                });
                await db.SaveChangesAsync();
            }

            ServiceProvider services = new ServiceCollection().BuildServiceProvider();
            CafeteriaReportService inner = new(factory, new NullAccountQuery(), services);
            IDMethodReportDecorator sut = new(inner, factory);

            ReportDocument report = await sut.GenerateAsync(
                CafeteriaReportKind.PurchaseItemDetail,
                new ReportFilter(new DateOnly(2026, 9, 10), new DateOnly(2026, 9, 10)));

            ReportTable detail = Assert.Single(report.Tables.Where(x => x.Title == "Itemized purchases"));
            Assert.Contains(detail.Columns, x => x.Key == "idMethod" && x.Heading == "ID Method");
            IReadOnlyDictionary<string, object?> row = Assert.Single(detail.Rows);
            Assert.Equal("NFC", row["idMethod"]);
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private sealed class NullAccountQuery : ICustomerAccountQuery
    {
        public Task<CustomerAccountView?> GetAsync(
            int customerId,
            int year,
            int month,
            CancellationToken cancellationToken = default)
            => Task.FromResult<CustomerAccountView?>(null);

        public Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(
            int customerId,
            int year,
            int month,
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CustomerMonthlyTransactionView>>([]);
    }
}
