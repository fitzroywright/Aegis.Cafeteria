namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;

public sealed class StandardWorkingDayProvider(IHolidayProvider holidayProvider) : IWorkingDayProvider
{
    public Task<int> GetWorkingDaysAsync(int year, int month, CancellationToken cancellationToken = default)
        => GetWorkingDaysAsync(year, month, null, cancellationToken);

    public async Task<int> GetWorkingDaysAsync(
        int year,
        int month,
        DateOnly? notBefore,
        CancellationToken cancellationToken = default)
    {
        if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        DateOnly monthStart = new(year, month, 1);
        DateOnly monthEnd = new(year, month, DateTime.DaysInMonth(year, month));
        DateOnly firstEligibleDate = notBefore.HasValue && notBefore.Value > monthStart
            ? notBefore.Value
            : monthStart;

        if (firstEligibleDate > monthEnd)
            return 0;

        HashSet<DateOnly> holidays = (await holidayProvider.GetHolidaysAsync(year, month, cancellationToken)).ToHashSet();
        int count = 0;
        for (DateOnly date = firstEligibleDate; date <= monthEnd; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (holidays.Contains(date)) continue;
            count++;
        }

        return count;
    }
}
