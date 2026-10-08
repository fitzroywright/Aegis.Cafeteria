namespace Aegis.Cafeteria.UnitTests.Application;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
public sealed class StipendServiceTests
{
    [Fact]
    public void Process_DeductsExactAmount()
    {
        var stipend = new StipendService(new Authorizer(true)).Process(Customer(), Snapshot(7000m), 5000m, "accounts.user", DateTimeOffset.UtcNow);
        Assert.Equal(5000m, stipend.Amount);
    }
    [Fact]
    public void Process_RejectsOverdraft()
    {
        Assert.Throws<InvalidOperationException>(() => new StipendService(new Authorizer(true)).Process(Customer(), Snapshot(3000m), 5000m, "accounts.user", DateTimeOffset.UtcNow));
    }
    [Fact]
    public void Process_RejectsUnauthorizedUser()
    {
        Assert.Throws<UnauthorizedAccessException>(() => new StipendService(new Authorizer(false)).Process(Customer(), Snapshot(7000m), 5000m, "unauthorized", DateTimeOffset.UtcNow));
    }
    private static Customer Customer() => new(10, "Test", null, true, 5000m);
    private static CustomerAccountSnapshot Snapshot(decimal available) => new(
        BaseMonthlyCredit: 7000m, PriorPeriodOutstandingSettlements: 0m, StipendCashPaid: 0m,
        StipendCardReduction: 0m, BaseCreditAvailableForCurrentPurchases: available,
        AdditionalCreditApproved: 0m, TotalSpendingCapacity: 7000m, TotalCharges: 0m,
        CompanyResponsibility: 0m, CompanyContributionUsed: 0m, RemainingCompanyContribution: 0m,
        CustomerResponsibilityForCurrentCharges: 0m, CustomerPayments: 0m, EmployeeAmountDue: 0m,
        AvailableCredit: available, AdditionalCreditUsed: 0m, UnusedAdditionalCredit: 0m);
    private sealed class Authorizer(bool allowed) : IStipendAuthorizer
    {
        public bool CanProcessStipend(string userId) => allowed;
    }
}
