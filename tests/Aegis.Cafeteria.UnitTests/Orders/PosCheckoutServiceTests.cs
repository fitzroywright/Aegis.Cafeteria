namespace Aegis.Cafeteria.UnitTests.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;

public sealed class PosCheckoutServiceTests
{
    [Fact]
    public async Task CompleteAsync_PersistsCompletedSplitTenderOrder()
    {
        var store = new FakeStore();
        var service = new PosCheckoutService(store);
        var view = View(available: 700m);

        var order = await service.CompleteAsync(
            view,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            cafeteriaAccountAmount: 700m,
            cashAmount: 300m,
            cashierUserId: "fwright",
            terminalId: "CAFETERIA-POS");

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(700m, order.CafeteriaAccountPaid);
        Assert.Equal(300m, order.CashPaid);
        Assert.Equal("fwright", order.CashierUserId);
        Assert.Equal("CAFETERIA-POS", order.TerminalId);
        Assert.Same(order, store.SavedOrder);
    }

    [Fact]
    public async Task CompleteAsync_RejectsAccountTenderAboveAvailableCredit()
    {
        var service = new PosCheckoutService(new FakeStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            View(available: 500m),
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            cafeteriaAccountAmount: 600m,
            cashAmount: 400m));
    }

    [Fact]
    public async Task CompleteAsync_RejectsTenderThatDoesNotEqualOrderTotal()
    {
        var service = new PosCheckoutService(new FakeStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            View(available: 1_000m),
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            cafeteriaAccountAmount: 500m,
            cashAmount: 400m));
    }


    [Fact]
    public async Task CompleteAsync_CashCustomer_RequiresCashOnly()
    {
        var store = new FakeStore();
        var service = new PosCheckoutService(store);
        var cash = CashCustomerAccountViewFactory.Create(new DateOnly(2026, 8, 24));

        var order = await service.CompleteAsync(
            cash,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            cafeteriaAccountAmount: 0m,
            cashAmount: 1_000m);

        Assert.Equal(1_000m, order.CashPaid);
        Assert.Equal(0m, order.CafeteriaAccountPaid);
        Assert.Same(order, store.SavedOrder);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            cash,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            cafeteriaAccountAmount: 1m,
            cashAmount: 999m));
    }


    [Fact]
    public async Task CompleteAsync_RejectsClosedAccountPeriod()
    {
        PosCheckoutService service = new(new FakeStore());
        CustomerAccountView view = View(available: 1_000m) with
        {
            Period = new AccountPeriodView(2026, 8, 20, 4_000m, 8_000m, true)
        };

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>
        (
            () => service.CompleteAsync
            (
                  view
                , [new PosOrderLine(1, "Lunch", 1_000m, 1)]
                , cafeteriaAccountAmount: 1_000m
                , cashAmount: 0m
                , cashierUserId: "fwright"
                , terminalId: "CAFETERIA-POS"
            )
        );

        Assert.Contains("closed cafeteria period", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CustomerAccountView View(decimal available) => new(
        new CustomerIdentityView(1, "Customer", null, true),
        new AccountPeriodView(2026, 8, 20, 4_000m, 8_000m, false),
        new CustomerFinancialStatusView(
            CreditLimit: 12_000m,
            SubsidyLimit: 8_000m,
            SubsidyUsed: 0m,
            SubsidyAmountRemaining: Math.Min(8_000m, available),
            NonSubsidyAmountRemaining: Math.Max(0m, available - 8_000m),
            TotalAvailableToSpend: available,
            CarriedForwardDebt: 0m,
            GrossCustomerLiability: 0m,
            TotalOrderSales: 0m,
            CafeteriaAccountSpending: 0m,
            CashSpending: 0m,
            AdditionalCreditApproved: 0m,
            AdditionalCreditUsed: 0m,
            AdditionalCreditRemaining: 0m,
            StipendCashPaid: 0m,
            StipendCardReduction: 0m,
            PriorPeriodSettlements: 0m,
            PriorPeriodRefundCredits: 0m,
            CustomerPayments: 0m,
            CustomerResponsibility: 0m,
            EmployeeAmountDue: 0m,
            TotalRefunds: 0m),
        [],
        new FfpPublicationStatus(true,0,0,DateTimeOffset.UtcNow,null));

    private sealed class FakeStore : ICafeteriaPersistenceStore
    {
        public Order? SavedOrder { get; private set; }
        public Task SaveOrderAsync(Order order, CancellationToken ct = default) { SavedOrder = order; return Task.CompletedTask; }
        public Task SaveCustomerAsync(Customer customer, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveCustomerIdentifierAsync(CustomerIdentifier identifier, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveMonthlyCreditAllocationAsync(MonthlyCreditAllocation allocation, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveProductAsync(Product product, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveAdditionalCreditApprovalAsync(AdditionalCreditApproval approval, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveCustomerPaymentAsync(CustomerPayment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveCarriedForwardDebtAsync(CarriedForwardDebt debt, CancellationToken ct = default) => Task.CompletedTask;
        public Task SavePriorPeriodOutstandingSettlementAsync(PriorPeriodOutstandingSettlement settlement, CancellationToken ct = default) => Task.CompletedTask;
        public Task SavePriorPeriodRefundCreditAsync(PriorPeriodRefundCredit credit, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveStipendAsync(Stipend stipend, CancellationToken ct = default) => Task.CompletedTask;
        public Task SaveAccountPeriodCloseAsync(AccountPeriodClose close, CancellationToken ct = default) => Task.CompletedTask;
    }
}
