namespace Aegis.Cafeteria.Domain.Accounts;

/// <summary>
/// Immutable monthly cafeteria entitlement.
/// Total credit = eligible working days x configured credit-limit LunchItem.Price (default FFP00).
/// Subsidy = eligible working days x configured subsidy LunchItem.Cost (default FFP00), capped at total credit.
/// </summary>
public sealed record MonthlyCreditAllocation
{
    public MonthlyCreditAllocation(
        int customerId,
        int year,
        int month,
        int eligibleWorkingDays,
        bool isEligibleForCredit,
        decimal creditLimitPrice,
        decimal subsidyCost,
        DateTimeOffset calculatedAt)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (year < 2000 || year > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        if (eligibleWorkingDays < 0) throw new ArgumentOutOfRangeException(nameof(eligibleWorkingDays));
        if (creditLimitPrice < 0m) throw new ArgumentOutOfRangeException(nameof(creditLimitPrice));
        if (subsidyCost < 0m) throw new ArgumentOutOfRangeException(nameof(subsidyCost));

        CustomerId = customerId;
        Year = year;
        Month = month;
        EligibleWorkingDays = eligibleWorkingDays;
        IsEligibleForCredit = isEligibleForCredit;
        CreditLimitPrice = isEligibleForCredit ? creditLimitPrice : 0m;
        SubsidyCost = isEligibleForCredit ? subsidyCost : 0m;
        CalculatedAt = calculatedAt;
    }

    public int CustomerId { get; }
    public int Year { get; }
    public int Month { get; }
    public int EligibleWorkingDays { get; }
    public bool IsEligibleForCredit { get; }
    public decimal CreditLimitPrice { get; }
    public decimal SubsidyCost { get; }
    public DateTimeOffset CalculatedAt { get; }

    public decimal TotalCreditLimit => IsEligibleForCredit ? EligibleWorkingDays * CreditLimitPrice : 0m;
    public decimal SubsidyAllocation => IsEligibleForCredit ? Math.Min(TotalCreditLimit, EligibleWorkingDays * SubsidyCost) : 0m;
    public decimal EmployeeFundedAllocation => Math.Max(0m, TotalCreditLimit - SubsidyAllocation);
}
