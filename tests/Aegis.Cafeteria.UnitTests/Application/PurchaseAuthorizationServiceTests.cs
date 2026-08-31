namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class PurchaseAuthorizationServiceTests
{
    [Fact]
    public void EnsureCanPurchase_WhenOrderFitsAvailableCredit_DoesNotThrow()
    {
        var service = new PurchaseAuthorizationService();
        var customer = CreateCustomer();
        var order = CreateOrder(customer.Id, 500m);
        var account = Snapshot(availableCredit: 600m);

        service.EnsureCanPurchase(customer, account, order);
    }

    [Fact]
    public void EnsureCanPurchase_WhenOrderExceedsAvailableCredit_Throws()
    {
        var service = new PurchaseAuthorizationService();
        var customer = CreateCustomer();
        var order = CreateOrder(customer.Id, 650m);
        var account = Snapshot(availableCredit: 600m);

        Assert.Throws<InvalidOperationException>(() =>
            service.EnsureCanPurchase(customer, account, order));
    }

    private static Customer CreateCustomer()
        => new(10, "Test Customer", null, true, 5_000m);

    private static Order CreateOrder(int customerId, decimal total)
    {
        var order = new Order(Guid.NewGuid(), customerId, DateTimeOffset.UtcNow);
        order.AddItem(new OrderItem(1, "Purchase", total, 1));
        return order;
    }

    private static CustomerAccountSnapshot Snapshot(decimal availableCredit)
        => new(
            BaseMonthlyCredit: 0m,
            PriorPeriodOutstandingSettlements: 0m,
            StipendCashPaid: 0m,
            StipendCardReduction: 0m,
            BaseCreditAvailableForCurrentPurchases: 0m,
            AdditionalCreditApproved: 0m,
            TotalSpendingCapacity: availableCredit,
            TotalCharges: 0m,
            CompanyResponsibility: 0m,
            CompanyContributionUsed: 0m,
            RemainingCompanyContribution: 0m,
            CustomerResponsibilityForCurrentCharges: 0m,
            CustomerPayments: 0m,
            EmployeeAmountDue: 0m,
            AvailableCredit: availableCredit,
            AdditionalCreditUsed: 0m,
            UnusedAdditionalCredit: 0m);
}
