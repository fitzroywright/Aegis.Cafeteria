namespace Aegis.Cafeteria.Application.Orders;

public static class TenderAllocationCalculator
{
    public static decimal CalculateCafeteriaAccountAmount
    (
          decimal total
        , decimal maximumCafeteriaAccountAmount
        , decimal cashReceived
    )
    {
        if (total < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(total));
        }

        if (maximumCafeteriaAccountAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumCafeteriaAccountAmount));
        }

        if (cashReceived < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(cashReceived));
        }

        decimal remainingAfterCash = Math.Max(0m, total - cashReceived);
        return Math.Min(maximumCafeteriaAccountAmount, remainingAfterCash);
    }
}
