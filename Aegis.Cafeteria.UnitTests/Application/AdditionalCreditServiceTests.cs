namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class AdditionalCreditServiceTests
{
    [Fact]
    public void Approve_WhenUserIsManager_CreatesApproval()
    {
        var service = new AdditionalCreditService(new StubAuthorizer(true));
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var approvedAt = new DateTimeOffset(2026, 8, 10, 10, 0, 0, TimeSpan.Zero);

        var approval = service.Approve(customer, period, [], 2_000m, "manager", approvedAt, "Temporary increase");

        Assert.Equal(customer.Id, approval.CustomerId);
        Assert.Equal(2_000m, approval.Amount);
        Assert.Equal("manager", approval.ApprovedBy);
        Assert.Equal("Temporary increase", approval.Reason);
    }

    [Fact]
    public void Approve_WhenUserIsNotManager_Throws()
    {
        var service = new AdditionalCreditService(new StubAuthorizer(false));
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);

        Assert.Throws<UnauthorizedAccessException>(() =>
            service.Approve(customer, period, [], 1_000m, "cashier", August(10)));
    }

    [Fact]
    public void Approve_WhenTotalWouldExceedCap_Throws()
    {
        var service = new AdditionalCreditService(new StubAuthorizer(true));
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var existing = new AdditionalCreditApproval(Guid.NewGuid(), customer.Id, 4_000m, "manager", August(5));

        Assert.Throws<InvalidOperationException>(() =>
            service.Approve(customer, period, [existing], 1_500m, "manager", August(10)));
    }



    [Fact]
    public void Approve_WhenCumulativeAmountEqualsEmployeeCap_AllowsApproval()
    {
        var service = new AdditionalCreditService(new StubAuthorizer(true));
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);
        var existing = new AdditionalCreditApproval(Guid.NewGuid(), customer.Id, 4_000m, "manager", August(5));

        var approval = service.Approve(customer, period, [existing], 1_000m, "manager", August(10), "Final approved increase");

        Assert.Equal(1_000m, approval.Amount);
    }

    [Fact]
    public void Approve_RejectsZeroOrNegativeAmount()
    {
        var service = new AdditionalCreditService(new StubAuthorizer(true));
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 8, 22);

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Approve(customer, period, [], 0m, "manager", August(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Approve(customer, period, [], -1m, "manager", August(10)));
    }

    [Fact]
    public void Approve_WhenOperationIdProvided_PreservesIdempotencyKey()
    {
        var service = new AdditionalCreditService(new StubAuthorizer(true));
        var operationId = Guid.NewGuid();

        var approval = service.Approve(CreateCustomer(), new AccountPeriod(2026, 8, 22), [],
            1_000m, "manager", August(10), "Approved", operationId);

        Assert.Equal(operationId, approval.Id);
    }

    private static Customer CreateCustomer()
        => new(10, "Test Customer", null, true, 5_000m);

    private static DateTimeOffset August(int day)
        => new(2026, 8, day, 12, 0, 0, TimeSpan.Zero);

    private sealed class StubAuthorizer(bool canApprove) : IAdditionalCreditAuthorizer
    {
        public bool CanApprove(string userId) => canApprove;
    }
}
