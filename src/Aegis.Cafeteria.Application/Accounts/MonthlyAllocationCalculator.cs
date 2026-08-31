namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

/// <summary>Pure shared month-opening calculation used by both POS fallback and Aegis.Cafeteria.Services.</summary>
public sealed class MonthlyAllocationCalculator
{
    public int CountWorkingDays(int year, int month, IEnumerable<DateOnly> holidays)
    {
        if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var excluded = holidays.ToHashSet();
        var count = 0;
        for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
        {
            var date = new DateOnly(year, month, day);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (excluded.Contains(date)) continue;
            count++;
        }
        return count;
    }

    public MonthlyCreditAllocation Calculate(int customerId, int year, int month, int workingDays, bool eligible, decimal creditLimitPrice, decimal subsidyCost, DateTimeOffset calculatedAt)
        => new(customerId, year, month, workingDays, eligible, creditLimitPrice, subsidyCost, calculatedAt);
}
