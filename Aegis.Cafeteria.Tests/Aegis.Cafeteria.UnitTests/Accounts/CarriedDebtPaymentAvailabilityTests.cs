namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class CarriedDebtPaymentAvailabilityTests
{
    [Fact]
    public void PaymentAgainstCarriedDebt_ReopensCurrentPeriodAvailability()
    {
        var periodDate = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);
        var customer = new Customer(1, "Customer", null, true, 5000m);
        var period = new AccountPeriod(2026, 9, 20);
        var allocation = new MonthlyCreditAllocation(1, 2026, 9, 20, true, 600m, 600m, periodDate);
        var debt = new CarriedForwardDebt(Guid.NewGuid(), 1, 2026, 8, 2026, 9, 1500m, periodDate);
        var calculator = new CustomerAccountCalculator();

        var before = calculator.Calculate(
            customer: customer, period: period, allocation: allocation, orders: [],
            additionalCreditApprovals: [], payments: [], carriedForwardDebts: [debt],
            priorPeriodSettlements: [], stipends: [], priorPeriodRefundCredits: []);
        var payment = new CustomerPayment(Guid.NewGuid(), 1, 1500m, periodDate, "Cashier");
        var after = calculator.Calculate(
            customer: customer, period: period, allocation: allocation, orders: [],
            additionalCreditApprovals: [], payments: [payment], carriedForwardDebts: [debt],
            priorPeriodSettlements: [], stipends: [], priorPeriodRefundCredits: []);

        Assert.Equal(allocation.TotalCreditLimit - 1500m, before.AvailableCredit);
        Assert.Equal(allocation.TotalCreditLimit, after.AvailableCredit);
        Assert.Equal(0m, after.EmployeeAmountDue);
    }
}
