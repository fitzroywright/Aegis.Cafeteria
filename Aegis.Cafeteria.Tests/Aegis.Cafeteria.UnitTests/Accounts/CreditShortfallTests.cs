namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class CreditShortfallTests
{
    [Fact]
    public void LateStipend_PreservesSignedShortfall_WithoutInventingAnotherDebtTransaction()
    {
        DateTimeOffset at = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);
        Customer customer = new(77, "Employee", null, true, 300m);
        MonthlyCreditAllocation allocation = new(77, 2026, 9, 1, true, 1000m, 600m, at);
        Order order = new(Guid.NewGuid(), 77, at);
        order.AddItem(new OrderItem(1, "Lunch", 600m, 1));
        order.Submit(at);
        order.Complete([new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 600m, at)], at, "cashier", "TEST");
        Stipend stipend = new(Guid.NewGuid(), 77, 600m, 600m, StipendPaymentMode.Advance, "manager", at);
        CustomerAccountCalculator calculator = new();
        CustomerAccountSnapshot snapshot = calculator.Calculate(customer, new AccountPeriod(2026, 9, 1), allocation, [order], [], [], [], [], [stipend]);
        Assert.Equal(-200m, snapshot.SignedAvailableCredit);
        Assert.Equal(0m, snapshot.TotalAvailableToSpend);
        Assert.Equal(200m, snapshot.CreditShortfall);
        Assert.Equal(600m, snapshot.EmployeeAmountDue);
        Assert.Equal(0m, snapshot.CurrentCreditSaleDebt);

        AdditionalCreditApproval approval = new(Guid.NewGuid(), 77, 300m, "manager", at, "Additional capacity");
        CustomerAccountSnapshot recovered = calculator.Calculate(customer, new AccountPeriod(2026, 9, 1), allocation, [order], [approval], [], [], [], [stipend]);
        Assert.Equal(100m, recovered.TotalAvailableToSpend);
        Assert.Equal(0m, recovered.CreditShortfall);
        Assert.Equal(snapshot.EmployeeAmountDue, recovered.EmployeeAmountDue);
    }
}
