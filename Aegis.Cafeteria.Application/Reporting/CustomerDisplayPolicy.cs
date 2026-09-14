namespace Aegis.Cafeteria.Application.Reporting;

public sealed record CustomerDisplayMonitor(string DeviceName, bool IsPrimary);

public static class CustomerDisplayPolicy
{
    public static bool UseMaximizedWindow => true;
    public static CustomerDisplayMonitor? SelectCustomerMonitor(IEnumerable<CustomerDisplayMonitor> monitors)
    {
        ArgumentNullException.ThrowIfNull(monitors);
        return monitors.FirstOrDefault(x => !x.IsPrimary);
    }


}
