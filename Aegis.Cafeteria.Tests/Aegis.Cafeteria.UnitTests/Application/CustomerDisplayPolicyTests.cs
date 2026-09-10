namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Reporting;

public sealed class CustomerDisplayPolicyTests
{
    [Fact]
    public void CustomerWindow_IsConfiguredForMaximizedKioskDisplay()
    {
        Assert.True(CustomerDisplayPolicy.UseMaximizedWindow);
    }

    [Fact]
    public void SelectCustomerMonitor_PrefersFirstNonPrimaryDisplay()
    {
        var result = CustomerDisplayPolicy.SelectCustomerMonitor([
            new CustomerDisplayMonitor("DISPLAY1", true),
            new CustomerDisplayMonitor("DISPLAY2", false),
            new CustomerDisplayMonitor("DISPLAY3", false)]);

        Assert.NotNull(result);
        Assert.Equal("DISPLAY2", result!.DeviceName);
    }

    [Fact]
    public void SelectCustomerMonitor_OneMonitor_ReturnsNull()
    {
        Assert.Null(CustomerDisplayPolicy.SelectCustomerMonitor([new CustomerDisplayMonitor("DISPLAY1", true)]));
    }

}
