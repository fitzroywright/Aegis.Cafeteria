namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class CustomerAccountCalculatorTests
{
    private readonly CustomerAccountCalculator _calculator = new();

    [Fact]
    public void BaseMonthlyCredit_IsRateTimesWorkingDays_AndIsAvailableImmediately()
    {
        var result = Calculate(CreateCustomer(), new AccountPeriod(2026, 8, 22));

        Assert.Equal(13_200m, result.BaseMonthlyCredit);
        Assert.Equal(13_200m, result.AvailableCredit);
    }

    [Fact]
    public void CustomerCanSpendEntireMonthlyCreditInOneDay()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var order = CreateOrder(customer.Id, August(1), 13_200m);

        var result = Calculate(customer, period, orders: [order]);

        Assert.Equal(13_200m, result.TotalCharges);
        Assert.Equal(0m, result.AvailableCredit);
    }

    [Fact]
    public void CompanyPaysAllCharges_WhenBelowContributionLimit()
    {
        var customer = CreateCustomer();
        var result = Calculate(
            customer,
            new AccountPeriod(2026, 8, 22),
            orders: [CreateOrder(customer.Id, August(5), 4_500m)]);

        Assert.Equal(4_500m, result.CompanyResponsibility);
        Assert.Equal(0m, result.CustomerResponsibilityForCurrentCharges);
        Assert.Equal(0m, result.EmployeeAmountDue);
    }

    [Fact]
    public void CustomerResponsibility_BecomesCurrentMonthEmployeeAmountDue()
    {
        var customer = CreateCustomer();
        var result = Calculate(
            customer,
            new AccountPeriod(2026, 8, 22),
            orders: [CreateOrder(customer.Id, August(5), 10_000m)]);

        Assert.Equal(8_000m, result.CompanyResponsibility);
        Assert.Equal(2_000m, result.CustomerResponsibilityForCurrentCharges);
        Assert.Equal(2_000m, result.EmployeeAmountDue);
    }

    [Fact]
    public void CustomerPayment_ReducesEmployeeAmountDue()
    {
        var customer = CreateCustomer();
        var result = Calculate(
            customer,
            new AccountPeriod(2026, 8, 22),
            orders: [CreateOrder(customer.Id, August(11), 10_000m)],
            payments: [new CustomerPayment(Guid.NewGuid(), customer.Id, 1_500m, August(15))]);

        Assert.Equal(2_000m, result.CustomerResponsibilityForCurrentCharges);
        Assert.Equal(1_500m, result.CustomerPayments);
        Assert.Equal(500m, result.EmployeeAmountDue);
    }

    [Fact]
    public void AdditionalCredit_IncreasesSpendingCapacity_ButNeverCompanyContribution()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var approval = new AdditionalCreditApproval(Guid.NewGuid(), customer.Id, 3_000m, "manager", August(10));
        var order = CreateOrder(customer.Id, August(11), 15_000m);

        var result = Calculate(customer, period, orders: [order], approvals: [approval]);

        Assert.Equal(16_200m, result.TotalSpendingCapacity);
        Assert.Equal(8_000m, result.CompanyResponsibility);
        Assert.Equal(7_000m, result.CustomerResponsibilityForCurrentCharges);
        Assert.Equal(1_800m, result.AdditionalCreditUsed);
        Assert.Equal(1_200m, result.UnusedAdditionalCredit);
    }

    [Fact]
    public void PriorPeriodSettlement_IsSeparateFromCurrentMonthCharges_AndReducesAvailableCredit()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 9, 21);
        var settlement = Settlement(customer.Id, 2026, 8, 2026, 9, 2_500m);

        var result = Calculate(customer, period, settlements: [settlement]);

        Assert.Equal(12_600m, result.BaseMonthlyCredit);
        Assert.Equal(2_500m, result.PriorPeriodOutstandingSettlements);
        Assert.Equal(10_100m, result.BaseCreditAvailableForCurrentPurchases);
        Assert.Equal(0m, result.TotalCharges);
        Assert.Equal(10_100m, result.AvailableCredit);
    }

    [Fact]
    public void PriorPeriodSettlement_DoesNotItselfReceiveCompanyContribution()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 9, 21);
        var settlement = Settlement(customer.Id, 2026, 8, 2026, 9, 2_500m);

        var result = Calculate(customer, period, settlements: [settlement]);

        Assert.Equal(0m, result.CompanyResponsibility);
        Assert.Equal(0m, result.CustomerResponsibilityForCurrentCharges);
        Assert.Equal(2_500m, result.EmployeeAmountDue);
    }

    [Fact]
    public void PriorPeriodSettlement_ReducesBaseCreditEligibleForCompanyContribution()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 9, 21);
        var settlement = Settlement(customer.Id, 2026, 8, 2026, 9, 6_000m);
        var order = CreateOrder(customer.Id, September(5), 8_000m);

        var result = Calculate(customer, period, orders: [order], settlements: [settlement]);

        // 12,600 base - 6,000 correction = 6,600 of base credit remains for new purchases.
        Assert.Equal(6_600m, result.BaseCreditAvailableForCurrentPurchases);
        Assert.Equal(6_600m, result.CompanyResponsibility);
        Assert.Equal(1_400m, result.CustomerResponsibilityForCurrentCharges);
    }

    [Fact]
    public void PriorPeriodSettlement_CanExceedBaseMonthlyCredit_AndConsumesAllCurrentAvailability()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 9, 21);
        var settlement = Settlement(customer.Id, 2026, 8, 2026, 9, 12_601m);

        var result = Calculate(customer, period, settlements: [settlement]);

        Assert.Equal(12_601m, result.PriorPeriodOutstandingSettlements);
        Assert.Equal(0m, result.BaseCreditAvailableForCurrentPurchases);
        Assert.Equal(0m, result.AvailableCredit);
        Assert.Equal(12_601m, result.EmployeeAmountDue);
        Assert.Equal(0m, result.CompanyResponsibility);
    }

    [Fact]
    public void Stipend_DeductsExactAmountFromAvailableCredit()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var stipend = new Stipend(Guid.NewGuid(), customer.Id, 800m, "accounts.user", August(5), "Approved");
        var result = Calculate(customer, period, stipends: [stipend]);
        Assert.Equal(800m, result.StipendCardReduction);
        Assert.Equal(12_400m, result.AvailableCredit);
        Assert.Equal(0m, result.StipendCashPaid);
    }

    [Fact]
    public void SplitTender_OnlyCafeteriaAccountPortionConsumesCardCreditAndSubsidy()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var when = August(12);
        var order = new Order(Guid.NewGuid(), customer.Id, when);
        order.AddItem(new OrderItem(1, "Purchase", 1_000m, 1));
        order.Submit(when);
        order.Complete(
            [
                new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, when),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, when)
            ],
            when);

        var result = Calculate(customer, period, orders: [order]);

        Assert.Equal(1_000m, result.TotalOrderSales);
        Assert.Equal(700m, result.TotalCharges);
        Assert.Equal(700m, result.CafeteriaAccountTendered);
        Assert.Equal(300m, result.CashTendered);
        Assert.Equal(700m, result.CompanyResponsibility);
        Assert.Equal(12_500m, result.AvailableCredit);
    }

    [Fact]
    public void CashOnlyOrder_DoesNotConsumeCafeteriaCreditOrSubsidy()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var when = August(12);
        var order = new Order(Guid.NewGuid(), customer.Id, when);
        order.AddItem(new OrderItem(1, "Cash Purchase", 1_000m, 1));
        order.Submit(when);
        order.Complete(
            [new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 1_000m, when)],
            when);

        var result = Calculate(customer, period, orders: [order]);

        Assert.Equal(1_000m, result.TotalOrderSales);
        Assert.Equal(0m, result.TotalCharges);
        Assert.Equal(1_000m, result.CashTendered);
        Assert.Equal(0m, result.CompanyResponsibility);
        Assert.Equal(13_200m, result.AvailableCredit);
    }

    [Fact]
    public void CafeteriaAccountRefund_RestoresCardCreditAndReversesAccountCharge()
    {
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var when = August(12);
        var order = new Order(Guid.NewGuid(), customer.Id, when);
        var item = new OrderItem(1, "Purchase", 1_000m, 1);
        order.AddItem(item);
        order.Submit(when);
        order.Complete(
            [
                new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, when),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, when)
            ],
            when);
        order.RecordRefund(new OrderRefund(
            Guid.NewGuid(),
            order.Id,
            [new OrderRefundItem(item.Id, 1, 1_000m)],
            [
                new RefundAllocation(PaymentMethod.CafeteriaAccount, 700m),
                new RefundAllocation(PaymentMethod.Cash, 300m)
            ],
            "manager",
            August(13),
            "Incorrect sale"));

        var result = Calculate(customer, period, orders: [order]);

        Assert.Equal(0m, result.TotalCharges);
        Assert.Equal(1_000m, result.TotalRefunds);
        Assert.Equal(700m, result.CafeteriaAccountRefunded);
        Assert.Equal(300m, result.CashRefunded);
        Assert.Equal(13_200m, result.AvailableCredit);
        Assert.Equal(0m, result.CompanyResponsibility);
    }

    private CustomerAccountSnapshot Calculate(
        Customer customer,
        AccountPeriod period,
        IEnumerable<Order>? orders = null,
        IEnumerable<AdditionalCreditApproval>? approvals = null,
        IEnumerable<CustomerPayment>? payments = null,
        IEnumerable<PriorPeriodOutstandingSettlement>? settlements = null,
        IEnumerable<Stipend>? stipends = null)
        => _calculator.Calculate(
            customer: customer,
            period: period,
            allocation: Allocation(customer.Id, period),
            orders: orders ?? [],
            additionalCreditApprovals: approvals ?? [],
            payments: payments ?? [],
            carriedForwardDebts: [],
            priorPeriodSettlements: settlements ?? [],
            stipends: stipends ?? [],
            priorPeriodRefundCredits: []);

    private static Customer CreateCustomer()
        => new(10, "Test Customer", null, true, 5_000m);

    private static MonthlyCreditAllocation Allocation(int customerId, AccountPeriod period)
        => new(customerId, period.Year, period.Month, period.WorkingDays, true, 600m, period.WorkingDays == 0 ? 0m : 8_000m / period.WorkingDays, DateTimeOffset.UtcNow);

    private static Order CreateOrder(int customerId, DateTimeOffset when, decimal amount)
    {
        var order = new Order(Guid.NewGuid(), customerId, when);
        order.AddItem(new OrderItem(1, "Purchase", amount, 1));
        order.Submit(when);
        order.Complete(
            [new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, amount, when)],
            when);
        return order;
    }

    private static PriorPeriodOutstandingSettlement Settlement(
        int customerId,
        int sourceYear,
        int sourceMonth,
        int settlementYear,
        int settlementMonth,
        decimal amount)
        => new(
            Guid.NewGuid(),
            customerId,
            sourceYear,
            sourceMonth,
            settlementYear,
            settlementMonth,
            amount,
            "manager",
            new DateTimeOffset(settlementYear, settlementMonth, 2, 10, 0, 0, TimeSpan.Zero),
            "Late outstanding balance discovered after period close.");

    private static DateTimeOffset August(int day)
        => new(2026, 8, day, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset September(int day)
        => new(2026, 9, day, 12, 0, 0, TimeSpan.Zero);
}
