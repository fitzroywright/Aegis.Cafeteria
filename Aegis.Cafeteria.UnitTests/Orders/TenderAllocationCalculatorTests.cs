namespace Aegis.Cafeteria.UnitTests.Orders;

using Aegis.Cafeteria.Application.Orders;

public sealed class TenderAllocationCalculatorTests
{
    [Fact]
    public void CashReceived_ReducesCafeteriaAccountAmountAutomatically()
    {
        decimal result = TenderAllocationCalculator.CalculateCafeteriaAccountAmount
        (
              1_000m
            , 1_000m
            , 300m
        );

        Assert.Equal(700m, result);
    }

    [Fact]
    public void RequiredCashGap_DoesNotReduceAlreadyLimitedAccountAmount()
    {
        decimal result = TenderAllocationCalculator.CalculateCafeteriaAccountAmount
        (
              1_000m
            , 600m
            , 200m
        );

        Assert.Equal(600m, result);
    }

    [Fact]
    public void CashCoveringWholeSale_ReducesAccountAmountToZero()
    {
        decimal result = TenderAllocationCalculator.CalculateCafeteriaAccountAmount
        (
              1_000m
            , 1_000m
            , 1_200m
        );

        Assert.Equal(0m, result);
    }
}
