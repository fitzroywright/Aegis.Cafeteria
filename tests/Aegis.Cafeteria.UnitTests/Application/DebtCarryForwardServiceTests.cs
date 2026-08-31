namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;

public sealed class DebtCarryForwardServiceTests
{
    [Fact]
    public void UnpaidPayroll_BecomesNextMonthDebt()
    {
        var result=new PayrollCollectionResult(9,2026,8,3_000m,1_000m,"payroll",DateTimeOffset.UtcNow);
        var debt=new DebtCarryForwardService().Create(result);
        Assert.NotNull(debt);
        Assert.Equal(2_000m,debt!.Amount);
        Assert.Equal(2026,debt.AppliedYear);
        Assert.Equal(9,debt.AppliedMonth);
    }

    [Fact]
    public void FullyCollectedPayroll_CreatesNoDebt()
    {
        var result=new PayrollCollectionResult(9,2026,8,3_000m,3_000m,"payroll",DateTimeOffset.UtcNow);
        Assert.Null(new DebtCarryForwardService().Create(result));
    }
}
