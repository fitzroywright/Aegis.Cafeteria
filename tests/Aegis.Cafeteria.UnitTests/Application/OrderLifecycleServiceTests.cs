namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Orders;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.UnitTests.TestDoubles;

public sealed class OrderLifecycleServiceTests
{
    [Fact]
    public void Complete_SplitTender_OnlyAuthorizesAccountPortionAgainstCardBalance()
    {
        var service = new OrderLifecycleService(new PurchaseAuthorizationService(), new AllowVoidAuthorizer());
        var customer = CreateCustomer();
        var order = service.Create(customer.Id, DateTimeOffset.UtcNow);
        order.AddItem(new OrderItem(1, "Purchase", 1_000m, 1));
        service.Submit(order, DateTimeOffset.UtcNow);

        service.Complete(
            customer,
            Snapshot(availableCredit: 700m),
            order,
            [
                new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, DateTimeOffset.UtcNow),
                new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, DateTimeOffset.UtcNow)
            ],
            DateTimeOffset.UtcNow);

        Assert.Equal(OrderStatus.Completed, order.Status);
    }

    [Fact]
    public void Complete_WhenAccountPortionExceedsAvailableCredit_Throws()
    {
        var service = new OrderLifecycleService(new PurchaseAuthorizationService(), new AllowVoidAuthorizer());
        var customer = CreateCustomer();
        var order = service.Create(customer.Id, DateTimeOffset.UtcNow);
        order.AddItem(new OrderItem(1, "Purchase", 1_000m, 1));
        service.Submit(order, DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            service.Complete(
                customer,
                Snapshot(availableCredit: 699m),
                order,
                [
                    new OrderPayment(Guid.NewGuid(), PaymentMethod.CafeteriaAccount, 700m, DateTimeOffset.UtcNow),
                    new OrderPayment(Guid.NewGuid(), PaymentMethod.Cash, 300m, DateTimeOffset.UtcNow)
                ],
                DateTimeOffset.UtcNow));
    }

    private static Customer CreateCustomer()
        => new(10, "Test Customer", null, true, 5_000m);

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
            PayrollDeductionRequired: 0m,
            AvailableCredit: availableCredit,
            AdditionalCreditUsed: 0m,
            UnusedAdditionalCredit: 0m);
}
