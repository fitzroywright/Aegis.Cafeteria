using Aegis.Cafeteria.Infrastructure.Configuration;

namespace Aegis.Cafeteria.UnitTests.Configuration;

public sealed class CafeteriaUiOptionsTests
{
    [Fact]
    public void Defaults_AreSuitableForCutoverStabilization()
    {
        CafeteriaUiOptions options = new();

        Assert.Equal(60, options.CustomerInactivityTimeoutSeconds);
        Assert.Equal(10, options.RecentTransactionCount);
        Assert.True(options.ShowFfpDataTools);
    }

    [Fact]
    public void UiOptions_CanBeDisabledOrOverridden()
    {
        Assert.Equal(0, new CafeteriaUiOptions { CustomerInactivityTimeoutSeconds = 0 }.CustomerInactivityTimeoutSeconds);
        Assert.Equal(45, new CafeteriaUiOptions { CustomerInactivityTimeoutSeconds = 45 }.CustomerInactivityTimeoutSeconds);
        Assert.Equal(0, new CafeteriaUiOptions { RecentTransactionCount = 0 }.RecentTransactionCount);
        Assert.Equal(25, new CafeteriaUiOptions { RecentTransactionCount = 25 }.RecentTransactionCount);
        Assert.False(new CafeteriaUiOptions { ShowFfpDataTools = false }.ShowFfpDataTools);
    }
}
