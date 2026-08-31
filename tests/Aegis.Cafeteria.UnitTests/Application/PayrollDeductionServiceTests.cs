namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;

public sealed class PayrollDeductionServiceTests
{
    [Fact]
    public void Create_UsesClosedPeriodPayrollAmount()
    {
        var period = new AccountPeriod(2026, 8, 22);
        var close = new AccountPeriodClose(Guid.NewGuid(), 42, period, 1750m, "payroll", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        var result = new PayrollDeductionService().Create(close);
        Assert.Equal(42, result.CustomerId);
        Assert.Equal(1750m, result.Amount);
    }
}
