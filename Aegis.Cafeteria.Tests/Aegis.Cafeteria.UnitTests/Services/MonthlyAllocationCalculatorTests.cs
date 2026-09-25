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

    [Fact]
    public void CountWorkingDays_CurrentMonthStarter_CountsOnlyRemainingEligibleDays()
    {
        var sut = new MonthlyAllocationCalculator();
        var startDate = new DateOnly(2026, 9, 14);
        var holidays = new[] { new DateOnly(2026, 9, 21) };

        var expected = Enumerable.Range(startDate.Day, DateTime.DaysInMonth(2026, 9) - startDate.Day + 1)
            .Select(day => new DateOnly(2026, 9, day))
            .Count(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && !holidays.Contains(date));

        Assert.Equal(expected, sut.CountWorkingDays(2026, 9, holidays, startDate));
    }

    [Fact]
    public void CountWorkingDays_EmployeeStartingAfterMonth_ReturnsZero()
    {
        var sut = new MonthlyAllocationCalculator();
        Assert.Equal(0, sut.CountWorkingDays(2026, 9, Array.Empty<DateOnly>(), new DateOnly(2026, 10, 1)));
    }

    [Fact]
    public void CountEligibleWorkingDays_UsesAuthoritativeCalendarAndEffectiveDate()
    {
        MonthlyAllocationCalculator sut = new();
        DateOnly[] workingDays =
        [
              new DateOnly(2026, 9, 14)
            , new DateOnly(2026, 9, 15)
            , new DateOnly(2026, 9, 16)
            , new DateOnly(2026, 9, 18)
            , new DateOnly(2026, 9, 21)
        ];

        int count = sut.CountEligibleWorkingDays(
            workingDays,
            new DateOnly(2026, 9, 16));

        Assert.Equal(3, count);
    }

    [Fact]
    public void CountEligibleWorkingDays_DoesNotInventWeekdaysMissingFromPolicy()
    {
        MonthlyAllocationCalculator sut = new();
        DateOnly[] workingDays =
        [
              new DateOnly(2026, 9, 14)
            , new DateOnly(2026, 9, 16)
        ];

        Assert.Equal(
            2,
            sut.CountEligibleWorkingDays(workingDays, null));
    }
}
