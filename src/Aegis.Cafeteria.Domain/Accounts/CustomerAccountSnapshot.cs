namespace Aegis.Cafeteria.Domain.Accounts;

public sealed record CustomerAccountSnapshot(
    decimal BaseMonthlyCredit,
    decimal PriorPeriodOutstandingSettlements,
    decimal StipendCashPaid,
    decimal StipendCardReduction,
    decimal BaseCreditAvailableForCurrentPurchases,
    decimal AdditionalCreditApproved,
    decimal TotalSpendingCapacity,
    decimal TotalCharges,
    decimal CompanyResponsibility,
    decimal CompanyContributionUsed,
    decimal RemainingCompanyContribution,
    decimal CustomerResponsibilityForCurrentCharges,
    decimal CustomerPayments,
    decimal EmployeeAmountDue,
    decimal AvailableCredit,
    decimal AdditionalCreditUsed,
    decimal UnusedAdditionalCredit,
    decimal TotalOrderSales = 0m,
    decimal CafeteriaAccountTendered = 0m,
    decimal CashTendered = 0m,
    decimal TotalRefunds = 0m,
    decimal CafeteriaAccountRefunded = 0m,
    decimal CashRefunded = 0m,
    decimal PriorPeriodRefundCredits = 0m,
    decimal MonthlyEmployeeFundedAllocation = 0m,
    decimal MonthlySubsidyAllocation = 0m,
    decimal CarriedForwardDebt = 0m,
    decimal GrossCustomerLiability = 0m)
{
    public decimal CreditLimit => BaseMonthlyCredit;
    public decimal SubsidyLimit => MonthlySubsidyAllocation > 0m ? MonthlySubsidyAllocation : CompanyContributionUsed + RemainingCompanyContribution;
    public decimal RemainingSubsidyEligibleBaseCredit => Math.Max(0m,
        BaseMonthlyCredit - Math.Max(0m, CarriedForwardDebt + PriorPeriodOutstandingSettlements + StipendCardReduction + TotalCharges - CustomerPayments));
    public decimal SubsidyAmountRemaining => Math.Min(RemainingCompanyContribution, RemainingSubsidyEligibleBaseCredit);
    public decimal NonSubsidyAmountRemaining => Math.Max(0m, AvailableCredit - SubsidyAmountRemaining);
    public decimal TotalAvailableToSpend => AvailableCredit;
}
