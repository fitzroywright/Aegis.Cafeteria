namespace Aegis.Cafeteria.Application.Reporting;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Identification;

public sealed record CustomerAccountView(
    CustomerIdentityView Customer,
    AccountPeriodView Period,
    CustomerFinancialStatusView FinancialStatus,
    IReadOnlyList<CustomerMonthlyTransactionView> Transactions,
    ExternalPublicationStatus ExternalPublication,
    CustomerFundingStatusView? Funding = null,
    CustomerIdentificationContext? Identification = null);

public sealed record CustomerIdentificationContext(
    IDMethod Method,
    string Code);

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
    bool IsCashCustomer = false,
    string EmailAddress = "");

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
    decimal TotalRefunds,
    decimal CurrentCreditSaleDebt = 0m,
    decimal CreditShortfall = 0m)
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
    Guid ReferenceId,
    decimal? OutstandingBalanceBefore = null,
    decimal? OutstandingBalanceAfter = null);
