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
        FakeStore store = new();
        PosCheckoutService service = new(store);
        CustomerAccountView view = View(available: 700m);

        Order order = await service.CompleteAsync(new PosCheckoutRequest(
            view,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 700m,
            CashAmount: 300m,
            CreditAmount: 0m,
            CashierUserId: "fwright",
            TerminalId: "CAFETERIA-POS"));

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(700m, order.CafeteriaAccountPaid);
        Assert.Equal(300m, order.CashPaid);
        Assert.Equal("fwright", order.CashierUserId);
        Assert.Equal("CAFETERIA-POS", order.TerminalId);
        Assert.Same(order, store.SavedOrder);
    }

    [Fact]
    public async Task CompleteAsync_PersistsCreditSaleForEligibleEmployee()
    {
        FakeStore store = new();
        PosCheckoutService service = new(store);
        CustomerAccountView view = View(available: 500m);
        PosCheckoutRequest request = new(
            view,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 500m,
            CashAmount: 200m,
            CreditAmount: 300m,
            CashierUserId: "fwright",
            TerminalId: "CAFETERIA-POS");

        Order order = await service.CompleteAsync(request);

        Assert.Equal(500m, order.CafeteriaAccountPaid);
        Assert.Equal(200m, order.CashPaid);
        Assert.Equal(300m, order.CreditPaid);
        Assert.Same(order, store.SavedOrder);
    }

    [Fact]
    public async Task CompleteAsync_AllowsEmployeeCreditWhenAllocationHasNoAvailableBalance()
    {
        FakeStore store = new();
        PosCheckoutService service = new(store);
        CustomerAccountView view = View(available: 0m);

        Order order = await service.CompleteAsync(new PosCheckoutRequest(
            view,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 0m,
            CashAmount: 0m,
            CreditAmount: 1_000m,
            CashierUserId: "fwright",
            TerminalId: "CAFETERIA-POS"));

        Assert.Equal(0m, order.CafeteriaAccountPaid);
        Assert.Equal(0m, order.CashPaid);
        Assert.Equal(1_000m, order.CreditPaid);
        Assert.Same(order, store.SavedOrder);
    }

    [Fact]
    public async Task CompleteAsync_CreditShortfallBlocksEmployeeCardButNotEmployeeCredit()
    {
        FakeStore store = new();
        PosCheckoutService service = new(store);
        CustomerAccountView view = View(available: 0m) with
        {
            FinancialStatus = View(available: 0m).FinancialStatus with { CreditShortfall = 250m }
        };

        Order creditOrder = await service.CompleteAsync(new PosCheckoutRequest(
            view,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 0m,
            CashAmount: 0m,
            CreditAmount: 1_000m,
            CashierUserId: "fwright",
            TerminalId: "CAFETERIA-POS"));

        Assert.Equal(1_000m, creditOrder.CreditPaid);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(new PosCheckoutRequest(
            view,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 1m,
            CashAmount: 999m,
            CreditAmount: 0m,
            CashierUserId: "fwright",
            TerminalId: "CAFETERIA-POS")));

        Assert.Contains("Employee Card", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CompleteAsync_RejectsCreditSaleForCashCustomer()
    {
        PosCheckoutService service = new(new FakeStore());
        CustomerAccountView cash = CashCustomerAccountViewFactory.Create(new DateOnly(2026, 9, 2));
        PosCheckoutRequest request = new(
            cash,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 0m,
            CashAmount: 900m,
            CreditAmount: 100m,
            CashierUserId: "fwright",
            TerminalId: "CAFETERIA-POS");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(request));
    }

    [Fact]
    public async Task CompleteAsync_InvokesPostSaleObserverAfterOrderIsSaved()
    {
        FakeStore store = new();
        RecordingObserver observer = new(store);
        PosCheckoutService service = new(store, [observer]);

        Order order = await service.CompleteAsync(
            new PosCheckoutRequest(
                View(1_000m, "employee@example.com"),
                [new PosOrderLine(1, "Lunch", 1_000m, 1)],
                1_000m,
                0m,
                0m,
                "fwright",
                "CAFETERIA-POS"));

        Assert.True(observer.Called);
        Assert.True(observer.OrderWasAlreadySaved);
        Assert.Same(order, observer.Order);
        Assert.Equal("employee@example.com", observer.Account?.Customer.EmailAddress);
    }

    [Fact]
    public async Task CompleteAsync_ObserverFailure_DoesNotFailCommittedSale()
    {
        FakeStore store = new();
        PosCheckoutService service = new(store, [new ThrowingObserver()]);

        Order order = await service.CompleteAsync(
            new PosCheckoutRequest(
                View(1_000m),
                [new PosOrderLine(1, "Lunch", 1_000m, 1)],
                1_000m,
                0m,
                0m,
                "fwright",
                "CAFETERIA-POS"));

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Same(order, store.SavedOrder);
    }

    [Fact]
    public async Task CompleteAsync_NoObservers_CompletesNormally()
    {
        FakeStore store = new();
        PosCheckoutService service = new(store, []);

        Order order = await service.CompleteAsync(
            new PosCheckoutRequest(
                View(1_000m),
                [new PosOrderLine(1, "Lunch", 1_000m, 1)],
                1_000m,
                0m,
                0m,
                "fwright",
                "CAFETERIA-POS"));

        Assert.Equal(OrderStatus.Completed, order.Status);
    }

    [Fact]
    public async Task CompleteAsync_RejectsAccountTenderAboveAvailableCredit()
    {
        PosCheckoutService service = new(new FakeStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(new PosCheckoutRequest(
            View(available: 500m),
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 600m,
            CashAmount: 400m, CreditAmount: 0m, CashierUserId: "legacy", TerminalId: Environment.MachineName)));
    }

    [Fact]
    public async Task CompleteAsync_RejectsTenderThatDoesNotEqualOrderTotal()
    {
        PosCheckoutService service = new(new FakeStore());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(new PosCheckoutRequest(
            View(available: 1_000m),
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 500m,
            CashAmount: 400m, CreditAmount: 0m, CashierUserId: "legacy", TerminalId: Environment.MachineName)));
    }

    [Fact]
    public async Task CompleteAsync_CashCustomer_RequiresCashOnly()
    {
        FakeStore store = new();
        PosCheckoutService service = new(store);
        CustomerAccountView cash = CashCustomerAccountViewFactory.Create(new DateOnly(2026, 8, 24));

        Order order = await service.CompleteAsync(new PosCheckoutRequest(
            cash,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 0m,
            CashAmount: 1_000m, CreditAmount: 0m, CashierUserId: "legacy", TerminalId: Environment.MachineName));

        Assert.Equal(1_000m, order.CashPaid);
        Assert.Equal(0m, order.CafeteriaAccountPaid);
        Assert.Same(order, store.SavedOrder);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(new PosCheckoutRequest(
            cash,
            [new PosOrderLine(1, "Lunch", 1_000m, 1)],
            CafeteriaAccountAmount: 1m,
            CashAmount: 999m, CreditAmount: 0m, CashierUserId: "legacy", TerminalId: Environment.MachineName)));
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
            () => service.CompleteAsync(new PosCheckoutRequest
            (
                  view
                , [new PosOrderLine(1, "Lunch", 1_000m, 1)]
                , CafeteriaAccountAmount: 1_000m
                , CashAmount: 0m
                , CreditAmount: 0m
                , CashierUserId: "fwright"
                , TerminalId: "CAFETERIA-POS"
            ))
        );

        Assert.Contains("closed cafeteria period", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CustomerAccountView View(decimal available, string emailAddress = "") => new(
        new CustomerIdentityView(1, "Customer", null, true, false, emailAddress),
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
        new ExternalPublicationStatus(true, 0, 0, DateTimeOffset.UtcNow, null));

    private sealed class RecordingObserver(FakeStore store) : IPostSaleObserver
    {
        public bool Called { get; private set; }
        public bool OrderWasAlreadySaved { get; private set; }
        public CustomerAccountView? Account { get; private set; }
        public Order? Order { get; private set; }

        public Task OnSaleCompletedAsync(CustomerAccountView account, Order order, CancellationToken cancellationToken = default)
        {
            this.Called = true;
            this.OrderWasAlreadySaved = ReferenceEquals(store.SavedOrder, order);
            this.Account = account;
            this.Order = order;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : IPostSaleObserver
    {
        public Task OnSaleCompletedAsync(CustomerAccountView account, Order order, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Observer failure must not fail checkout.");
    }

    private sealed class SequenceObserver(List<string> sequence) : IPostSaleObserver
    {
        public Task OnSaleCompletedAsync(CustomerAccountView account, Order order, CancellationToken cancellationToken = default)
        {
            sequence.Add("observer");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore(List<string>? sequence = null) : ICafeteriaPersistenceStore
    {
        public Order? SavedOrder { get; private set; }

        public Task SaveOrderAsync(Order order, CancellationToken cancellationToken = default)
        {
            this.SavedOrder = order;
            sequence?.Add("save");
            return Task.CompletedTask;
        }

        public Task SaveCustomerAsync(Customer customer, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCustomerIdentifierAsync(CustomerIdentifier identifier, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveMonthlyCreditAllocationAsync(MonthlyCreditAllocation allocation, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveProductAsync(Product product, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAdditionalCreditApprovalAsync(AdditionalCreditApproval approval, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCustomerPaymentAsync(CustomerPayment payment, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveCarriedForwardDebtAsync(CarriedForwardDebt debt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SavePriorPeriodOutstandingSettlementAsync(PriorPeriodOutstandingSettlement settlement, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SavePriorPeriodRefundCreditAsync(PriorPeriodRefundCredit credit, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveStipendAsync(Stipend stipend, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SaveAccountPeriodCloseAsync(AccountPeriodClose close, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

