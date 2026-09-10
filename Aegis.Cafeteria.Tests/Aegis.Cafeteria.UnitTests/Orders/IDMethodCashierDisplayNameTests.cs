namespace Aegis.Cafeteria.UnitTests.Orders;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Domain.Products;

public sealed class IDMethodCashierDisplayNameTests
{
    [Fact]
    public async Task Checkout_PersistsAuthenticatedCashierDisplayNameAndIdentificationContext()
    {
        RecordingStore store = new();
        OperatorSessionStub session = new(new OperatorIdentity(
            "FFPJM\\fwright",
            "Fitzroy Wright",
            false,
            DateTimeOffset.UtcNow));
        PosCheckoutService service = new(store, operatorSession: session);
        CustomerAccountView account = new(
            new CustomerIdentityView(42, "Employee", null, true),
            new AccountPeriodView(2026, 9, 20, 0m, 0m, false),
            new CustomerFinancialStatusView(
                5000m, 0m, 0m, 0m, 5000m, 5000m, 0m, 0m, 0m, 0m, 0m,
                0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m),
            [],
            new FfpPublicationStatus(true, 0, 0, null, null),
            Identification: new CustomerIdentificationContext(IDMethod.Name, "NAME SEARCH"));

        Order order = await service.CompleteAsync(new PosCheckoutRequest(
            account,
            [new PosOrderLine(1, "Lunch", 500m, 1)],
            500m,
            0m,
            0m,
            "FFPJM\\fwright",
            "POS01"));

        Assert.Same(order, store.SavedOrder);
        Assert.Equal("FFPJM\\fwright", order.CashierUserId);
        Assert.Equal("Fitzroy Wright", order.CashierName);
        Assert.Equal(IDMethod.Name, order.IDMethod);
        Assert.Equal("NAME SEARCH", order.IdentificationCode);
    }

    private sealed class OperatorSessionStub(OperatorIdentity identity) : IOperatorSession
    {
        public OperatorIdentity? Current { get; private set; } = identity;
        public bool IsAuthenticated => Current is not null;
        public void SignIn(OperatorIdentity value) => Current = value;
        public void SignOut() => Current = null;
    }

    private sealed class RecordingStore : ICafeteriaPersistenceStore
    {
        public Order? SavedOrder { get; private set; }
        public Task SaveOrderAsync(Order order, CancellationToken ct = default)
        {
            SavedOrder = order;
            return Task.CompletedTask;
        }

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
