namespace Aegis.Cafeteria.UnitTests.Reporting;

using Aegis.Cafeteria.Application.Reporting;

public sealed class FundingUiContractTests
{
    [Fact]
    public void FundingStatusView_DoesNotExposeSyntheticDailyRate()
    {
        var names = typeof(CustomerFundingStatusView).GetProperties().Select(x => x.Name).ToHashSet();
        Assert.DoesNotContain("DailyCreditRate", names);
        Assert.DoesNotContain("CompanySubsidyDailyRate", names);
    }

    [Fact]
    public void AccountPeriodView_ExposesAllocationsButNotSyntheticDailyRate()
    {
        var names = typeof(AccountPeriodView).GetProperties().Select(x => x.Name).ToHashSet();
        Assert.Contains("SubsidyAllocation", names);
        Assert.DoesNotContain("DailyCreditRate", names);
        Assert.DoesNotContain("CompanySubsidyDailyRate", names);
    }
}
