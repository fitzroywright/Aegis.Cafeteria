namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

/// <summary>Pure shared month-opening calculation used by both POS fallback and Aegis.Cafeteria.Services.</summary>
public sealed class MonthlyAllocationCalculator
{
    public int CountWorkingDays(int year, int month, IEnumerable<DateOnly> holidays)
        => CountWorkingDays(year, month, holidays, null);

    public int CountWorkingDays(int year, int month, IEnumerable<DateOnly> holidays, DateOnly? employmentStartDate)
    {
        if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        DateOnly monthStart = new(year, month, 1);
        DateOnly monthEnd = new(year, month, DateTime.DaysInMonth(year, month));
        DateOnly effectiveStart = employmentStartDate.HasValue && employmentStartDate.Value > monthStart
            ? employmentStartDate.Value
            : monthStart;

        if (effectiveStart > monthEnd)
            return 0;

        HashSet<DateOnly> excluded = holidays.ToHashSet();
        int count = 0;
        for (DateOnly date = effectiveStart; date <= monthEnd; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (excluded.Contains(date)) continue;
            count++;
        }

        return count;
    }

    public int CountEligibleWorkingDays(
        IEnumerable<DateOnly> workingDayCalendar,
        DateOnly? eligibleFrom)
    {
        ArgumentNullException.ThrowIfNull(workingDayCalendar);

        DateOnly[] workingDays = workingDayCalendar
            .Distinct()
            .OrderBy(date => date)
            .ToArray();

        if (workingDays.Length == 0)
            return 0;

        DateOnly effectiveStart = eligibleFrom ?? workingDays[0];
        return workingDays.Count(date => date >= effectiveStart);
    }

    public MonthlyCreditAllocation Calculate(int customerId, int year, int month, int workingDays, bool eligible, decimal creditLimitPrice, decimal subsidyCost, DateTimeOffset calculatedAt)
        => new(customerId, year, month, workingDays, eligible, creditLimitPrice, subsidyCost, calculatedAt);
}
