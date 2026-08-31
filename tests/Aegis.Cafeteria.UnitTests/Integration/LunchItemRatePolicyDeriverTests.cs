namespace Aegis.Cafeteria.UnitTests.Integration;

using Aegis.Cafeteria.Application.Integration;

public sealed class LunchItemRatePolicyDeriverTests
{
    [Fact]
    public void Derive_UsesConfiguredCreditAndSubsidyCodes_AndExcludesBothFromSaleCatalog()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = LunchItemRatePolicyDeriver.Derive(
            [
                new ExternalProductSnapshot(1, "FFP00", "Cafeteria Policy", 800m, 600m),
                new ExternalProductSnapshot(3, "LUNCH", "Lunch", 750m, 500m)
            ],
            now,
            creditLimitLunchItemCode: "FFP00",
            subsidyLunchItemCode: "FFP00");

        Assert.Equal("FFP00", snapshot.CreditLimitLunchItemCode);
        Assert.Equal(800m, snapshot.CreditLimitPrice);
        Assert.Equal("FFP00", snapshot.SubsidyLunchItemCode);
        Assert.Equal(600m, snapshot.SubsidyCost);
        var sale = Assert.Single(snapshot.Products);
        Assert.Equal("LUNCH", sale.Code);
    }
}
