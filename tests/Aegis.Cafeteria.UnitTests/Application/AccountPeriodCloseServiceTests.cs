namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.UnitTests.TestDoubles;

public sealed class AccountPeriodCloseServiceTests
{
    [Fact]
    public void Close_CapturesFinalPayrollDeduction()
    {
        var service = new AccountPeriodCloseService(new AllowCloseAuthorizer());
        var period = new AccountPeriod(2026, 8, 22);
        var result = service.Close(Customer(), period, Snapshot(1250m), "payroll.manager", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        Assert.Equal(1250m, result.PayrollDeduction);
        Assert.Equal("payroll.manager", result.ClosedBy);
    }

    [Fact]
    public void Close_WhenUnauthorized_Throws()
    {
        var service = new AccountPeriodCloseService(new DenyCloseAuthorizer());
        Assert.Throws<UnauthorizedAccessException>(() => service.Close(Customer(), new AccountPeriod(2026, 8, 22), Snapshot(0), "user", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Close_BeforePeriodEnd_Throws()
    {
        var service = new AccountPeriodCloseService(new AllowCloseAuthorizer());
        Assert.Throws<InvalidOperationException>(() => service.Close(Customer(), new AccountPeriod(2026, 8, 22), Snapshot(0), "manager", new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero)));
    }

    private static Customer Customer() => new(1, "Customer", null, true, 5_000m);
    private static CustomerAccountSnapshot Snapshot(decimal payroll) => new(0,0,0,0,0,0,0,0,0,0,0,0,0,payroll,0,0,0);
}
