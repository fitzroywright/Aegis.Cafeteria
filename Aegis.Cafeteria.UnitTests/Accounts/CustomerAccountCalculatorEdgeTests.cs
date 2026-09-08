namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class CustomerAccountCalculatorEdgeTests
{
    private readonly CustomerAccountCalculator _calculator = new();

    [Fact]
    public void ZeroWorkingDays_ProducesZeroBaseCredit()
    {
        var result = Calculate(new AccountPeriod(2026, 8, 0));
        Assert.Equal(0m, result.BaseMonthlyCredit);
        Assert.Equal(0m, result.AvailableCredit);
    }

    [Fact]
    public void PurchaseExactlyEqualToAvailableCredit_IsAllowedByCalculation()
    {
        var period = new AccountPeriod(2026, 8, 1);
        var order = CardOrder(600m, new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero));
        var result = Calculate(period, [order]);
        Assert.Equal(0m, result.AvailableCredit);
    }

    [Fact]
    public void PriorPeriodRefundCredit_IncreasesCurrentAvailableCreditWithoutReopeningSourcePeriod()
    {
        var period = new AccountPeriod(2026, 9, 1);
        var credit = new PriorPeriodRefundCredit(Guid.NewGuid(), 10, Guid.NewGuid(), Guid.NewGuid(), 2026, 8, 2026, 9, 650m, "manager", new DateTimeOffset(2026, 9, 2, 8, 0, 0, TimeSpan.Zero), "Late refund");
        var result = _calculator.Calculate(
            customer: Customer(), period: period, allocation: Allocation(period), orders: [],
            additionalCreditApprovals: [], payments: [], carriedForwardDebts: [],
            priorPeriodSettlements: [], stipends: [], priorPeriodRefundCredits: [credit]);
        Assert.Equal(1_250m, result.AvailableCredit);
        Assert.Equal(650m, result.PriorPeriodRefundCredits);
    }

    private CustomerAccountSnapshot Calculate(AccountPeriod period, IEnumerable<Order>? orders = null)
        => _calculator.Calculate(
            customer: Customer(), period: period, allocation: Allocation(period), orders: orders ?? [],
            additionalCreditApprovals: [], payments: [], carriedForwardDebts: [],
            priorPeriodSettlements: [], stipends: [], priorPeriodRefundCredits: []);
    private static Customer Customer() => new(10, "Customer", null, true, 5000m);
    private static MonthlyCreditAllocation Allocation(AccountPeriod period) => new(10, period.Year, period.Month, period.WorkingDays, true, 600m, period.WorkingDays == 0 ? 0m : 8000m / period.WorkingDays, DateTimeOffset.UtcNow);
    private static Order CardOrder(decimal amount, DateTimeOffset when)
    {
        var order = new Order(Guid.NewGuid(), 10, when);
        order.AddItem(new OrderItem(1, "Purchase", amount, 1));
        order.Submit(when);
        order.Complete([new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, amount, when)], when);
        return order;
    }
}
