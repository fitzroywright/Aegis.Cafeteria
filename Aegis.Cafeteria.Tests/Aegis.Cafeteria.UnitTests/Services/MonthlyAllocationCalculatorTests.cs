namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Application.Accounts;

public sealed class MonthlyAllocationCalculatorTests
{
    [Fact]
    public void Calculate_UsesFfp00PriceForCreditAndCostForSubsidy()
    {
        var sut = new MonthlyAllocationCalculator();
        var x = sut.Calculate(101, 2026, 8, 20, true, 900m, 650m, DateTimeOffset.UtcNow);
        Assert.Equal(18_000m, x.TotalCreditLimit);
        Assert.Equal(13_000m, x.SubsidyAllocation);
        Assert.Equal(5_000m, x.EmployeeFundedAllocation);
    }

    [Fact]
    public void CountWorkingDays_ExcludesWeekendsAndHolidays()
    {
        var sut = new MonthlyAllocationCalculator();
        var holidays = new[] { new DateOnly(2026, 8, 6) };
        var expected = Enumerable.Range(1, DateTime.DaysInMonth(2026,8))
            .Select(d=>new DateOnly(2026,8,d))
            .Count(d=>d.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(d));
        Assert.Equal(expected, sut.CountWorkingDays(2026, 8, holidays));
    }
}
