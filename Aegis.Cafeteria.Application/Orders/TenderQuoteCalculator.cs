namespace Aegis.Cafeteria.Application.Orders;

public sealed record TenderQuote
(
      decimal Total
    , decimal MaximumCafeteriaAccountAmount
    , decimal CafeteriaAccountAmount
    , decimal CashReceived
    , decimal CashApplied
    , decimal CreditAmount
    , decimal RemainingAmount
    , decimal ChangeAmount
    , decimal NewCustomerResponsibility
    , bool CreditSalesAllowed
    , bool IsComplete
);

public static class TenderQuoteCalculator
{
    public static TenderQuote Calculate
    (
          decimal total
        , decimal availableToSpend
        , decimal subsidyRemaining
        , bool cafeteriaAccountAllowed
        , bool creditSalesAllowed
        , decimal cafeteriaAccountAmount
        , decimal cashReceived
        , bool useEmployeeCredit
    )
    {
        if (total < 0m) throw new ArgumentOutOfRangeException(nameof(total));
        if (availableToSpend < 0m) throw new ArgumentOutOfRangeException(nameof(availableToSpend));
        if (subsidyRemaining < 0m) throw new ArgumentOutOfRangeException(nameof(subsidyRemaining));
        if (cafeteriaAccountAmount < 0m) throw new ArgumentOutOfRangeException(nameof(cafeteriaAccountAmount));
        if (cashReceived < 0m) throw new ArgumentOutOfRangeException(nameof(cashReceived));

        decimal maximumAccount = cafeteriaAccountAllowed
            ? Math.Min(total, availableToSpend)
            : 0m;

        if (cafeteriaAccountAmount > maximumAccount)
        {
            throw new InvalidOperationException("The cafeteria-account amount exceeds the maximum available amount.");
        }

        decimal cashNeeded = Math.Max(0m, total - cafeteriaAccountAmount);
        decimal cashApplied = Math.Min(cashReceived, cashNeeded);
        decimal uncovered = Math.Max(0m, total - cafeteriaAccountAmount - cashApplied);
        decimal creditAmount = creditSalesAllowed && useEmployeeCredit
            ? uncovered
            : 0m;
        decimal remaining = Math.Max(0m, uncovered - creditAmount);
        decimal change = Math.Max(0m, cashReceived - cashNeeded);
        decimal employeeCardResponsibility = Math.Max(0m, cafeteriaAccountAmount - subsidyRemaining);
        decimal newResponsibility = employeeCardResponsibility + creditAmount;

        return new TenderQuote
        (
              total
            , maximumAccount
            , cafeteriaAccountAmount
            , cashReceived
            , cashApplied
            , creditAmount
            , remaining
            , change
            , newResponsibility
            , creditSalesAllowed
            , remaining == 0m
        );
    }

    public static decimal DefaultCafeteriaAccountAmount
    (
          decimal total
        , decimal availableToSpend
        , bool cafeteriaAccountAllowed
        , decimal cashReceived = 0m
    )
    {
        decimal maximum = cafeteriaAccountAllowed
            ? Math.Min(total, Math.Max(0m, availableToSpend))
            : 0m;

        return TenderAllocationCalculator.CalculateCafeteriaAccountAmount(total, maximum, Math.Max(0m, cashReceived));
    }
}
