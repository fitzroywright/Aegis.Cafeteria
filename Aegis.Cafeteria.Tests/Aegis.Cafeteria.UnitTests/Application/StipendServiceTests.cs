namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;

public sealed class StipendServiceTests
{
    [Fact]
    public void Process_WhenAuthorized_CreatesAdvanceUsingAvailableCompanyContribution()
    {
        var service = new StipendService(new Authorizer(true));
        var customer = CreateCustomer();
        var account = Snapshot(availableCredit: 13_200m, remainingContribution: 8_000m);

        var stipend = service.Process(
            customer,
            account,
            cashAmount: 800m,
            StipendPaymentMode.Advance,
            "accounts.user",
            DateTimeOffset.UtcNow);

        Assert.Equal(800m, stipend.CashAmount);
        Assert.Equal(800m, stipend.CardReduction);
    }

    [Fact]
    public void Process_CapsCardReductionAtRemainingCompanyContribution()
    {
        var service = new StipendService(new Authorizer(true));
        var customer = CreateCustomer();
        var account = Snapshot(availableCredit: 5_000m, remainingContribution: 650m);

        var stipend = service.Process(
            customer,
            account,
            cashAmount: 800m,
            StipendPaymentMode.Reimbursement,
            "accounts.user",
            DateTimeOffset.UtcNow,
            expenseReportReference: "ER-42");

        Assert.Equal(800m, stipend.CashAmount);
        Assert.Equal(650m, stipend.CardReduction);
        Assert.Equal(StipendPaymentMode.Reimbursement, stipend.PaymentMode);
    }

    [Fact]
    public void Process_WhenFundingPeriodIsPriorMonth_BindsStipendToFundingPeriod()
    {
        var service = new StipendService(new Authorizer(true));
        var processedAt = new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

        var stipend = service.Process(
            CreateCustomer(),
            Snapshot(availableCredit: 5_000m, remainingContribution: 2_000m),
            cashAmount: 800m,
            StipendPaymentMode.Reimbursement,
            "accounts.user",
            processedAt,
            expenseReportReference: "ER-AUG-31",
            notes: "August expense paid in September",
            fundingYear: 2026,
            fundingMonth: 8);

        Assert.Equal(2026, stipend.FundingYear);
        Assert.Equal(8, stipend.FundingMonth);
        Assert.True(stipend.IsPriorPeriodAdjustment);
        Assert.Equal(800m, stipend.CardReduction);
    }

    [Fact]
    public void Process_WhenUnauthorized_Throws()
    {
        var service = new StipendService(new Authorizer(false));

        Assert.Throws<UnauthorizedAccessException>(() =>
            service.Process(
                CreateCustomer(),
                Snapshot(13_200m, 8_000m),
                800m,
                StipendPaymentMode.Advance,
                "not.accounts",
                DateTimeOffset.UtcNow));
    }

    private static Customer CreateCustomer()
        => new(10, "Test Customer", null, true, 5_000m);

    private static CustomerAccountSnapshot Snapshot(decimal availableCredit, decimal remainingContribution)
        => new(
            BaseMonthlyCredit: 13_200m,
            PriorPeriodOutstandingSettlements: 0m,
            StipendCashPaid: 0m,
            StipendCardReduction: 0m,
            BaseCreditAvailableForCurrentPurchases: 13_200m,
            AdditionalCreditApproved: 0m,
            TotalSpendingCapacity: 13_200m,
            TotalCharges: 0m,
            CompanyResponsibility: 0m,
            CompanyContributionUsed: 8_000m - remainingContribution,
            RemainingCompanyContribution: remainingContribution,
            CustomerResponsibilityForCurrentCharges: 0m,
            CustomerPayments: 0m,
            EmployeeAmountDue: 0m,
            AvailableCredit: availableCredit,
            AdditionalCreditUsed: 0m,
            UnusedAdditionalCredit: 0m);

    private sealed class Authorizer(bool allowed) : IStipendAuthorizer
    {
        public bool CanProcessStipend(string userId) => allowed;
    }
}
