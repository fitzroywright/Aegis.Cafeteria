using Aegis.Cafeteria.Infrastructure.Configuration;

namespace Aegis.Cafeteria.UnitTests.Configuration;

public sealed class CafeteriaUiOptionsTests
{
    [Fact]
    public void DefaultCustomerInactivityTimeout_IsThirtySeconds()
    {
        var options = new CafeteriaUiOptions();
        Assert.Equal(30, options.CustomerInactivityTimeoutSeconds);
        Assert.Equal(10, options.RecentTransactionCount);
    }

    [Fact]
    public void CustomerInactivityTimeout_CanBeDisabledOrOverridden()
    {
        Assert.Equal(0, new CafeteriaUiOptions { CustomerInactivityTimeoutSeconds = 0 }.CustomerInactivityTimeoutSeconds);
        Assert.Equal(45, new CafeteriaUiOptions { CustomerInactivityTimeoutSeconds = 45 }.CustomerInactivityTimeoutSeconds);
        Assert.Equal(0, new CafeteriaUiOptions { RecentTransactionCount = 0 }.RecentTransactionCount);
        Assert.Equal(25, new CafeteriaUiOptions { RecentTransactionCount = 25 }.RecentTransactionCount);
    }
}
