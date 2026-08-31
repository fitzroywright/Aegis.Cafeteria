namespace Aegis.Cafeteria.UnitTests.Integration;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class ExternalLedgerProjectionServiceTests
{
    [Fact]
    public async Task MonthlyAllocation_PublishesFfpCompatibilityProjectionAndBalances()
    {
        var gateway = new RecordingOutbox(); var sut = new ExternalLedgerProjectionService(gateway);
        var customer = new Customer(42,"Employee",null,true,0m);
        var allocation = new MonthlyCreditAllocation(42,2026,9,20,true,300m,200m,new DateTimeOffset(2026,9,1,8,0,0,TimeSpan.Zero));
        await sut.PublishMonthlyAllocationAsync(customer, allocation);
        var x = Assert.Single(gateway.Entries);
        Assert.Equal(ExternalLedgerEntryKind.MonthlyAllocation,x.Kind); Assert.Equal(6000m,x.CreditLimit); Assert.Equal(6000m,x.AvailableBalance);
        Assert.Equal("20 new Tickets Assigned to Employee By SYSTEM", x.Description);
        Assert.Equal("SYSTEM", x.UserName);
        Assert.Equal("Tickets Assigned", x.IdName);
    }

    [Fact]
    public async Task Purchase_PublishesOnlyAccountTender_AndPostBalance()
    {
        var gateway = new RecordingOutbox(); var sut = new ExternalLedgerProjectionService(gateway);
        var account = View(42, 5000m, 3000m);
        var order = new Order(Guid.NewGuid(),42,DateTimeOffset.Now); order.AddItem(new OrderItem(1,"Lunch",1000m,1)); order.Submit(DateTimeOffset.Now);
        order.Complete([new OrderPayment(Guid.NewGuid(),PaymentMethod.CafeteriaAccount,700m,DateTimeOffset.Now),new OrderPayment(Guid.NewGuid(),PaymentMethod.Cash,300m,DateTimeOffset.Now)],DateTimeOffset.Now,"FWright","POS01");
        await sut.PublishCompletedOrderAsync(account,order);
        var x=Assert.Single(gateway.Entries); Assert.Equal(700m,x.ScanValue); Assert.Equal(2300m,x.AvailableBalance);
        Assert.Equal("FWright", x.UserName);
        Assert.Equal("Lunch x1 - Order Total: 1,000.00; Cafeteria: 700.00", x.Description);
        Assert.Equal("Cafeteria Purchase", x.IdName);
    }

    [Fact]
    public async Task Refund_PreservesManagerUserAndProductDescription()
    {
        var gateway = new RecordingOutbox(); var sut = new ExternalLedgerProjectionService(gateway);
        var account = View(42, 5000m, 2300m);
        var order = new Order(Guid.NewGuid(),42,DateTimeOffset.Now);
        var line = new OrderItem(1,"Lunch",1000m,1);
        order.AddItem(line); order.Submit(DateTimeOffset.Now);
        order.Complete([new OrderPayment(Guid.NewGuid(),PaymentMethod.CafeteriaAccount,1000m,DateTimeOffset.Now)],DateTimeOffset.Now,"Cashier1","POS01");
        var refund = new OrderRefund(Guid.NewGuid(), order.Id, [new OrderRefundItem(line.Id,1,1000m)], [new RefundAllocation(PaymentMethod.CafeteriaAccount,1000m)], "Manager1", DateTimeOffset.Now, "Test");
        order.RecordRefund(refund);
        await sut.PublishRefundAsync(account, order, refund);
        var x=Assert.Single(gateway.Entries);
        Assert.Equal("Manager1",x.UserName);
        Assert.Equal("Refund - Lunch x1 - Cafeteria Refund: 1,000.00",x.Description);
        Assert.Equal("Cafeteria Refund",x.IdName);
    }

    private static CustomerAccountView View(int id, decimal limit, decimal available) => new(
        new CustomerIdentityView(id, "Employee", null, true),
        new AccountPeriodView(2026, 9, 20, 0m, 0m, false),
        new CustomerFinancialStatusView(
            CreditLimit: limit, SubsidyLimit: 0m, SubsidyUsed: 0m, SubsidyAmountRemaining: 0m,
            NonSubsidyAmountRemaining: available, TotalAvailableToSpend: available, CarriedForwardDebt: 0m,
            GrossCustomerLiability: 0m, TotalOrderSales: 0m, CafeteriaAccountSpending: 0m, CashSpending: 0m,
            AdditionalCreditApproved: 0m, AdditionalCreditUsed: 0m, AdditionalCreditRemaining: 0m,
            StipendCashPaid: 0m, StipendCardReduction: 0m, PriorPeriodSettlements: 0m, PriorPeriodRefundCredits: 0m,
            CustomerPayments: 0m, CustomerResponsibility: 0m, PayrollDeductionRequired: 0m, TotalRefunds: 0m),
        [],
        new FfpPublicationStatus(Enabled: true, PendingOperations: 0, PoisonedOperations: 0, LastSuccessfulPublication: null, LastError: null));

    private sealed class RecordingOutbox : IExternalLedgerOutbox
    {
        public List<ExternalLedgerEntry> Entries { get; } = [];
        public Task EnqueueAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
