namespace Aegis.Cafeteria.ReportingTests;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Transactions;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Reporting;
using Microsoft.EntityFrameworkCore;

public sealed class IDMethodReportTests
{
    [Fact]
    public async Task TransactionDetail_IncludesIDMethod()
    {
        await WithDatabaseAsync(async (factory, transactionId, at) =>
        {
            await InsertTransactionAsync(factory, transactionId, at, IdentificationMethod.Code, "ABC123");
            IDMethodReportDecorator decorator = Decorator(factory, new StubReportService(
                new TransactionDetailReport("Transaction Detail", at, at, 1, 100m, 0m, 0m, 100m,
                [new TransactionDetailRow(transactionId, at, "Lunch", "Customer", "Cashier", 100m, 0m, 0m, 100m)])));
            TransactionDetailReport report = await decorator.GetTransactionDetailAsync(Filter(at));
            TransactionDetailRow row = Assert.Single(report.Rows);
            Assert.Equal("Code", row.IDMethod);
        });
    }

    [Fact]
    public async Task ItemizedSales_IncludesIDMethod()
    {
        await WithDatabaseAsync(async (factory, transactionId, at) =>
        {
            await InsertTransactionAsync(factory, transactionId, at, IdentificationMethod.NFC, "TAG-1");
            IDMethodReportDecorator decorator = Decorator(factory, new StubReportService(itemized:
                new ItemizedSalesReport("Itemized Sales", at, at, 1, 100m,
                [new ItemizedSalesRow(transactionId, at, "Customer", "Cashier", "Lunch", 1m, 100m, 100m, "Cash")])));
            ItemizedSalesReport report = await decorator.GetItemizedSalesAsync(Filter(at));
            Assert.Equal("NFC", Assert.Single(report.Rows).IDMethod);
        });
    }

    [Fact]
    public async Task AccountStatement_IncludesIDMethod()
    {
        await WithDatabaseAsync(async (factory, transactionId, at) =>
        {
            await InsertTransactionAsync(factory, transactionId, at, IdentificationMethod.TRN, "123456789");
            CustomerMonthlyTransactionView transaction = new(transactionId, at, "Lunch", "Lunch", 100m, 0m, 100m, 0m, 0m, 0m, 0m, "Cashier", null, false, null, null);
            IDMethodReportDecorator decorator = Decorator(factory, new StubReportService(statement:
                new AccountStatementReport("Account Statement", at, at, 1, "Customer", 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, [transaction])));
            AccountStatementReport report = await decorator.GetAccountStatementAsync(1, Filter(at));
            Assert.Equal("TRN", Assert.Single(report.Transactions).IDMethod);
        });
    }

    [Fact]
    public async Task WeeklyCashVsAccount_IncludesIdentificationBreakdown()
    {
        await WithDatabaseAsync(async (factory, transactionId, at) =>
        {
            await InsertTransactionAsync(factory, transactionId, at, IdentificationMethod.NameSearch, "NAME SEARCH");
            IDMethodReportDecorator decorator = Decorator(factory, new StubReportService(weekly:
                new WeeklyCashVsAccountReport("Weekly", at, at, 1, 100m, 0m, 100m,
                [new WeeklyCashVsAccountRow(DateOnly.FromDateTime(at.LocalDateTime), 100m, 0m, 100m)])));
            WeeklyCashVsAccountReport report = await decorator.GetWeeklyCashVsAccountAsync(Filter(at));
            IdentificationMethodSummaryRow row = Assert.Single(report.IdentificationMethods!);
            Assert.Equal("NameSearch", row.IDMethod);
            Assert.Equal(1, row.TransactionCount);
        });
    }

    private static async Task InsertTransactionAsync(LocalDbContextFactory factory, Guid transactionId, DateTimeOffset at, IdentificationMethod method, string code)
    {
        await using LocalDbContext db = await factory.CreateDbContextAsync();
        db.Transactions.Add(new TransactionRecord
        {
            TransactionId = transactionId,
            OccurredAt = at,
            TransactionType = "Lunch",
            CustomerId = 1,
            CustomerName = "Customer",
            CashierName = "Cashier",
            GrossAmount = 100m,
            CashAmount = 100m,
            AccountAmount = 0m,
            NetAmount = 100m,
            IdentificationMethod = method,
            IdentificationCode = code
        });
        await db.SaveChangesAsync();
    }

    private static IDMethodReportDecorator Decorator(LocalDbContextFactory factory, IReportService inner) => new(inner, factory);
    private static ReportFilter Filter(DateTimeOffset at) { DateOnly date = DateOnly.FromDateTime(at.LocalDateTime); return new ReportFilter(date, date); }

    private static async Task WithDatabaseAsync(Func<LocalDbContextFactory, Guid, DateTimeOffset, Task> test)
    {
        string file = Path.Combine(Path.GetTempPath(), $"cafeteria-idmethod-report-{Guid.NewGuid():N}.db");
        try
        {
            CafeteriaPersistenceOptions options = new() { LocalSqliteConnectionString = $"Data Source={file}", TerminalId = "TEST" };
            await using LocalDbContextFactory factory = new(DbContextOptionsFactory.CreateLocal(options));
            await test(factory, Guid.NewGuid(), new DateTimeOffset(2026, 9, 10, 12, 30, 0, TimeSpan.Zero));
        }
        finally
        {
            if (File.Exists(file)) File.Delete(file);
        }
    }

    private sealed class StatementAccountQuery(DateTimeOffset at, CustomerMonthlyTransactionView transaction) : ICustomerAccountQuery
    {
        public Task<CustomerAccountView?> GetAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<CustomerAccountView?>(new CustomerAccountView(new CustomerIdentityView(customerId,"Fitzroy Wright",null,true),new AccountPeriodView(year,month,20,0m,0m,false),new CustomerFinancialStatusView(0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m),[transaction],new FfpPublicationStatus(false,0,0,null,null)));
    }

    private sealed class StubReportService(TransactionDetailReport? detail=null,ItemizedSalesReport? itemized=null,WeeklyCashVsAccountReport? weekly=null,AccountStatementReport? statement=null) : IReportService
    {
        public Task<TransactionDetailReport> GetTransactionDetailAsync(ReportFilter filter,CancellationToken cancellationToken=default)=>Task.FromResult(detail!);
        public Task<ItemizedSalesReport> GetItemizedSalesAsync(ReportFilter filter,CancellationToken cancellationToken=default)=>Task.FromResult(itemized!);
        public Task<WeeklyCashVsAccountReport> GetWeeklyCashVsAccountAsync(ReportFilter filter,CancellationToken cancellationToken=default)=>Task.FromResult(weekly!);
        public Task<AccountStatementReport> GetAccountStatementAsync(int customerId,ReportFilter filter,CancellationToken cancellationToken=default)=>Task.FromResult(statement!);
        public Task<DailyCloseoutReport> GetDailyCloseoutAsync(ReportFilter filter,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<WeeklyCloseoutReport> GetWeeklyCloseoutAsync(ReportFilter filter,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<SummaryReport> GetSummaryAsync(ReportFilter filter,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
}
