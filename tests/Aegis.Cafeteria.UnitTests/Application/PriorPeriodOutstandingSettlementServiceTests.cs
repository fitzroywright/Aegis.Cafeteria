namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class PriorPeriodOutstandingSettlementServiceTests
{
    [Fact]
    public void Approve_WhenUserAuthorized_CreatesAuditableSettlement()
    {
        var service = new PriorPeriodOutstandingSettlementService(new Authorizer(true));
        var customer = CreateCustomer();
        var period = new AccountPeriod(2026, 9, 21);
        var approvedAt = new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero);

        var settlement = service.Approve(
            customer,
            period,
            2026,
            8,
            2_500m,
            "manager",
            approvedAt,
            "August payroll deduction was not recorded.");

        Assert.Equal(customer.Id, settlement.CustomerId);
        Assert.Equal(2_500m, settlement.Amount);
        Assert.Equal(8, settlement.SourceMonth);
        Assert.Equal(9, settlement.SettlementMonth);
        Assert.Equal("manager", settlement.ApprovedBy);
    }

    [Fact]
    public void Approve_WhenUserUnauthorized_Throws()
    {
        var service = new PriorPeriodOutstandingSettlementService(new Authorizer(false));

        Assert.Throws<UnauthorizedAccessException>(() =>
            service.Approve(
                CreateCustomer(),
                new AccountPeriod(2026, 9, 21),
                2026,
                8,
                500m,
                "clerk",
                new DateTimeOffset(2026, 9, 2, 10, 0, 0, TimeSpan.Zero),
                "Late correction"));
    }

    private static Customer CreateCustomer()
        => new(10, "Test Customer", null, true, 5_000m);

    private sealed class Authorizer(bool canApprove) : IPriorPeriodSettlementAuthorizer
    {
        public bool CanApprove(string userId) => canApprove;
    }
}
