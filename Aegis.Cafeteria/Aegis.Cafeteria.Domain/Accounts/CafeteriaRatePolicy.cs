namespace Aegis.Cafeteria.Domain.Accounts;

/// <summary>
/// FFP-owned cafeteria funding policy cached locally for offline monthly allocation calculations.
/// Credit limit and company subsidy are deliberately separate policy values.
/// </summary>
public sealed record CafeteriaRatePolicy
{
    public CafeteriaRatePolicy(
        string creditLimitLunchItemCode,
        decimal creditLimitPrice,
        string subsidyLunchItemCode,
        decimal subsidyCost,
        DateTimeOffset refreshedAt)
    {
        if (string.IsNullOrWhiteSpace(creditLimitLunchItemCode)) throw new ArgumentException("Credit-limit LunchItem code is required.", nameof(creditLimitLunchItemCode));
        if (string.IsNullOrWhiteSpace(subsidyLunchItemCode)) throw new ArgumentException("Subsidy LunchItem code is required.", nameof(subsidyLunchItemCode));
        if (creditLimitPrice <= 0m) throw new ArgumentOutOfRangeException(nameof(creditLimitPrice), "Credit-limit LunchItem Price must be positive.");
        if (subsidyCost <= 0m) throw new ArgumentOutOfRangeException(nameof(subsidyCost), "Subsidy LunchItem Cost must be positive.");

        CreditLimitLunchItemCode = creditLimitLunchItemCode.Trim();
        CreditLimitPrice = creditLimitPrice;
        SubsidyLunchItemCode = subsidyLunchItemCode.Trim();
        SubsidyCost = subsidyCost;
        RefreshedAt = refreshedAt;
    }

    public string CreditLimitLunchItemCode { get; }
    public decimal CreditLimitPrice { get; }
    public string SubsidyLunchItemCode { get; }
    public decimal SubsidyCost { get; }
    public DateTimeOffset RefreshedAt { get; }
}
