namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class CustomerAccountSnapshotDisplayTests
{
    [Fact]
    public void FinancialDisplayAmounts_PartitionAvailableCredit()
    {
        var snapshot = new CustomerAccountSnapshot(
            BaseMonthlyCredit: 12_000m,
            PriorPeriodOutstandingSettlements: 0m,
            StipendCashPaid: 0m,
            StipendCardReduction: 0m,
            BaseCreditAvailableForCurrentPurchases: 12_000m,
            AdditionalCreditApproved: 1_000m,
            TotalSpendingCapacity: 13_000m,
            TotalCharges: 650m,
            CompanyResponsibility: 650m,
            CompanyContributionUsed: 650m,
            RemainingCompanyContribution: 7_350m,
            CustomerResponsibilityForCurrentCharges: 0m,
            CustomerPayments: 0m,
            EmployeeAmountDue: 0m,
            AvailableCredit: 12_350m,
            AdditionalCreditUsed: 0m,
            UnusedAdditionalCredit: 1_000m);

        Assert.Equal(7_350m, snapshot.SubsidyAmountRemaining);
        Assert.Equal(5_000m, snapshot.NonSubsidyAmountRemaining);
        Assert.Equal(snapshot.AvailableCredit, snapshot.SubsidyAmountRemaining + snapshot.NonSubsidyAmountRemaining);
        Assert.Equal(snapshot.AvailableCredit, snapshot.TotalAvailableToSpend);
    }

    [Fact]
    public void SubsidyRemaining_IsCappedByRemainingEligibleBaseCredit()
    {
        var snapshot = new CustomerAccountSnapshot(
            BaseMonthlyCredit: 5_000m,
            PriorPeriodOutstandingSettlements: 4_000m,
            StipendCashPaid: 0m,
            StipendCardReduction: 0m,
            BaseCreditAvailableForCurrentPurchases: 1_000m,
            AdditionalCreditApproved: 2_000m,
            TotalSpendingCapacity: 7_000m,
            TotalCharges: 500m,
            CompanyResponsibility: 500m,
            CompanyContributionUsed: 500m,
            RemainingCompanyContribution: 7_500m,
            CustomerResponsibilityForCurrentCharges: 0m,
            CustomerPayments: 0m,
            EmployeeAmountDue: 0m,
            AvailableCredit: 2_500m,
            AdditionalCreditUsed: 0m,
            UnusedAdditionalCredit: 2_000m);

        Assert.Equal(500m, snapshot.SubsidyAmountRemaining);
        Assert.Equal(2_000m, snapshot.NonSubsidyAmountRemaining);
    }
}
