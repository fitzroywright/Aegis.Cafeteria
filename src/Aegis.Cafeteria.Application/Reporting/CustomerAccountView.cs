namespace Aegis.Cafeteria.Application.Reporting;

using Aegis.Cafeteria.Application.Abstractions;

public sealed record CustomerAccountView(
    CustomerIdentityView Customer,
    AccountPeriodView Period,
    CustomerFinancialStatusView FinancialStatus,
    IReadOnlyList<CustomerMonthlyTransactionView> Transactions,
    FfpPublicationStatus FfpPublication,
    CustomerFundingStatusView? Funding = null);

public sealed record CustomerFundingStatusView(
    bool PolicyLoaded,
    bool IsAuthoritative,
    DateTimeOffset? PolicyRefreshedAt,
    string StatusMessage);

public sealed record CustomerIdentityView(
    int Id,
    string Name,
    byte[]? Photo,
    bool IsEligibleForCredit,
    bool IsCashCustomer = false);

public sealed record AccountPeriodView(
    int Year,
    int Month,
    int EligibleWorkingDays,
    decimal EmployeeFundedAllocation,
    decimal SubsidyAllocation,
    bool IsClosed);

public sealed record CustomerFinancialStatusView(
    decimal CreditLimit,
    decimal SubsidyLimit,
    decimal SubsidyUsed,
    decimal SubsidyAmountRemaining,
    decimal NonSubsidyAmountRemaining,
    decimal TotalAvailableToSpend,
    decimal CarriedForwardDebt,
    decimal CurrentCreditSaleDebt,
    decimal GrossCustomerLiability,
    decimal TotalOrderSales,
    decimal CafeteriaAccountSpending,
    decimal CashSpending,
    decimal AdditionalCreditApproved,
    decimal AdditionalCreditUsed,
    decimal AdditionalCreditRemaining,
    decimal StipendCashPaid,
    decimal StipendCardReduction,
    decimal PriorPeriodSettlements,
    decimal PriorPeriodRefundCredits,
    decimal CustomerPayments,
    decimal CustomerResponsibility,
    decimal EmployeeAmountDue,
    decimal TotalRefunds)
{
    public decimal TotalCreditLimit => CreditLimit + AdditionalCreditApproved + PriorPeriodRefundCredits;
}

public sealed record CustomerMonthlyTransactionView(
    DateTimeOffset OccurredAt,
    string Kind,
    string Description,
    decimal Amount,
    decimal CafeteriaAccountAmount,
    decimal CashAmount,
    Guid ReferenceId);
