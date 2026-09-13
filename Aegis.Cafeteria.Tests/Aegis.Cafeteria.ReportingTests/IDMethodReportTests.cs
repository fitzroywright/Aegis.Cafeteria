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
        await WithDatabaseAsync(async (factory, orderId, completedAt) => { await SeedCompletedOrderAsync(factory,orderId,completedAt,(int)IDMethod.NFC,"04A1B2C3"); IDMethodReportDecorator sut=Create(factory,new NullAccountQuery()); ReportDocument report=await sut.GenerateAsync(CafeteriaReportKind.PurchaseItemDetail,Filter(completedAt)); ReportTable detail=Assert.Single(report.Tables.Where(x=>x.Title=="Itemized purchases")); Assert.Contains(detail.Columns,x=>x.Key=="idMethod"&&x.Heading=="ID Method"); Assert.Equal("NFC",Assert.Single(detail.Rows)["idMethod"]); });
    }
    [Fact]
    public async Task PurchaseItemDetail_LegacyOrderLeavesIDMethodBlank()
    {
        await WithDatabaseAsync(async (factory,orderId,completedAt)=>{await SeedCompletedOrderAsync(factory,orderId,completedAt,null,null);IDMethodReportDecorator sut=Create(factory,new NullAccountQuery());ReportDocument report=await sut.GenerateAsync(CafeteriaReportKind.PurchaseItemDetail,Filter(completedAt));Assert.Equal(string.Empty,Assert.Single(Assert.Single(report.Tables.Where(x=>x.Title=="Itemized purchases")).Rows)["idMethod"]);});
    }
    [Fact]
    public async Task EmployeeStatement_UsesOriginatingOrderIDMethod()
    {
        await WithDatabaseAsync(async(factory,orderId,completedAt)=>{await SeedCompletedOrderAsync(factory,orderId,completedAt,(int)IDMethod.Code,"EMP0042");CustomerMonthlyTransactionView transaction=new(completedAt,"Purchase","Lunch",500m,500m,0m,orderId);IDMethodReportDecorator sut=Create(factory,new StatementAccountQuery(transaction));ReportDocument report=await sut.GenerateAsync(CafeteriaReportKind.EmployeeStatement,Filter(completedAt) with {EmployeeId=42});ReportTable table=Assert.Single(report.Tables);Assert.Contains(table.Columns,x=>x.Key=="idMethod"&&x.Heading=="ID Method");Assert.Equal("Code",Assert.Single(table.Rows)["idMethod"]);});
    }
    [Fact]
    public async Task RefundVoidAudit_UsesOriginatingOrderIDMethod()
    {
        await WithDatabaseAsync(async(factory,orderId,completedAt)=>{Guid itemId=Guid.NewGuid();Guid refundId=Guid.NewGuid();DateTimeOffset refundedAt=completedAt.AddMinutes(5);await using(LocalCafeteriaDbContext db=factory.CreateDbContext()){await db.Database.EnsureCreatedAsync();db.Customers.Add(Customer(completedAt));db.Orders.Add(new OrderRow{Id=orderId,CustomerId=42,CreatedAt=completedAt.AddMinutes(-1),Status=(int)OrderStatus.Completed,SubmittedAt=completedAt.AddSeconds(-30),CompletedAt=completedAt,AccountYear=2026,AccountMonth=9,CashierUserId="fwright",CashierName="Fitzroy Wright",TerminalId="POS01",IDMethod=(int)IDMethod.TRN,IdentificationCode="123456789",Revision=1,UpdatedAt=refundedAt,Items=[new OrderItemRow{Id=itemId,OrderId=orderId,ProductId=1,ProductName="Lunch",UnitPrice=500m,Quantity=1}],Payments=[new OrderPaymentRow{Id=Guid.NewGuid(),OrderId=orderId,Method=(int)PaymentMethod.CafeteriaAccount,Amount=500m,RecordedAt=completedAt}],Refunds=[new OrderRefundRow{Id=refundId,OrderId=orderId,RefundedBy="Manager",RefundedAt=refundedAt,Reason="Test",Items=[new OrderRefundItemRow{RefundId=refundId,OrderItemId=itemId,Quantity=1,UnitPrice=500m}]}]});await db.SaveChangesAsync();}IDMethodReportDecorator sut=Create(factory,new NullAccountQuery());ReportDocument report=await sut.GenerateAsync(CafeteriaReportKind.RefundVoidAudit,new ReportFilter(DateOnly.FromDateTime(refundedAt.LocalDateTime),DateOnly.FromDateTime(refundedAt.LocalDateTime)));ReportTable table=Assert.Single(report.Tables);Assert.Contains(table.Columns,x=>x.Key=="idMethod"&&x.Heading=="ID Method");Assert.Equal("TRN",Assert.Single(table.Rows)["idMethod"]);});
    }
    private static async Task SeedCompletedOrderAsync(LocalDbContextFactory factory,Guid orderId,DateTimeOffset completedAt,int? idMethod,string? identificationCode){await using LocalCafeteriaDbContext db=factory.CreateDbContext();await db.Database.EnsureCreatedAsync();db.Customers.Add(Customer(completedAt));db.Orders.Add(new OrderRow{Id=orderId,CustomerId=42,CreatedAt=completedAt.AddMinutes(-1),Status=(int)OrderStatus.Completed,SubmittedAt=completedAt.AddSeconds(-30),CompletedAt=completedAt,AccountYear=2026,AccountMonth=9,CashierUserId="fwright",CashierName="Fitzroy Wright",TerminalId="POS01",IDMethod=idMethod,IdentificationCode=identificationCode,Revision=1,UpdatedAt=completedAt,Items=[new OrderItemRow{Id=Guid.NewGuid(),OrderId=orderId,ProductId=1,ProductName="Lunch",UnitPrice=500m,Quantity=1}],Payments=[new OrderPaymentRow{Id=Guid.NewGuid(),OrderId=orderId,Method=(int)PaymentMethod.CafeteriaAccount,Amount=500m,RecordedAt=completedAt}]});await db.SaveChangesAsync();}
    private static CustomerRow Customer(DateTimeOffset at)=>new(){Id=42,Name="Fitzroy Wright",NormalizedName="FITZROY WRIGHT",Department="IT",IsEligibleForCredit=true,Revision=1,UpdatedAt=at};
    private static IDMethodReportDecorator Create(LocalDbContextFactory factory,ICustomerAccountQuery accountQuery){ServiceProvider services=new ServiceCollection().BuildServiceProvider();CafeteriaReportService inner=new(factory,accountQuery,services);return new IDMethodReportDecorator(inner,factory);}
    private static ReportFilter Filter(DateTimeOffset at){DateOnly date=DateOnly.FromDateTime(at.LocalDateTime);return new ReportFilter(date,date);}
    private static async Task WithDatabaseAsync(Func<LocalDbContextFactory,Guid,DateTimeOffset,Task> test){string file=Path.Combine(Path.GetTempPath(),$"cafeteria-idmethod-report-{Guid.NewGuid():N}.db");try{CafeteriaPersistenceOptions options=new(){LocalSqliteConnectionString=$"Data Source={file}",TerminalId="TEST"};LocalDbContextFactory factory=new(DbContextOptionsFactory.CreateLocal(options));await test(factory,Guid.NewGuid(),new DateTimeOffset(2026,9,10,12,30,0,TimeSpan.Zero));}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(File.Exists(file))File.Delete(file);}}
    private sealed class StatementAccountQuery(CustomerMonthlyTransactionView transaction):ICustomerAccountQuery{public Task<CustomerAccountView?> GetAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<CustomerAccountView?>(new CustomerAccountView(new CustomerIdentityView(customerId,"Fitzroy Wright",null,true),new AccountPeriodView(year,month,20,0m,0m,false),new CustomerFinancialStatusView(0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m,0m),[transaction],new FfpPublicationStatus(false,0,0,null,null)));public Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<CustomerMonthlyTransactionView>>([transaction]);}
    private sealed class NullAccountQuery:ICustomerAccountQuery{public Task<CustomerAccountView?> GetAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<CustomerAccountView?>(null);public Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(int customerId,int year,int month,CancellationToken cancellationToken=default)=>Task.FromResult<IReadOnlyList<CustomerMonthlyTransactionView>>([]);}
}
