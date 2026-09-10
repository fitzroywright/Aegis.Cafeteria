namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class CustomerAccountStatementServiceTests
{
    [Fact]
    public void Statement_SeparatesCashAndCafeteriaAccountActivity()
    {
        var customer = new Customer(1, "Customer", null, true, 5000m);
        var period = new AccountPeriod(2026, 8, 22);
        var when = new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
        var order = new Order(Guid.NewGuid(), 1, when);
        order.AddItem(new OrderItem(1, "Lunch", 1000m, 1));
        order.Submit(when);
        order.Complete([
            new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, when),
            new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, when)], when);
        var allocation = new MonthlyCreditAllocation(customer.Id, period.Year, period.Month, period.WorkingDays, true, 600m, 600m, DateTimeOffset.UtcNow);
        var snapshot = new CustomerAccountCalculator().Calculate(
            customer: customer,
            period: period,
            allocation: allocation,
            orders: [order],
            additionalCreditApprovals: [],
            payments: [],
            carriedForwardDebts: [],
            priorPeriodSettlements: [],
            stipends: [],
            priorPeriodRefundCredits: []);

        var statement = new CustomerAccountStatementService().Build(
            customer, period, snapshot, [order], [], [], [], [], []);

        Assert.Contains(statement.Entries, x => x.Category == "Purchase" && x.Amount == 700m);
        Assert.Contains(statement.Entries, x => x.Category == "Cash" && x.Amount == 300m);
    }
}
