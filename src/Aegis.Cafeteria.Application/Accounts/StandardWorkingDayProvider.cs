namespace Aegis.Cafeteria.Application.Accounts;

using Aegis.Cafeteria.Application.Abstractions;

public sealed class StandardWorkingDayProvider(IHolidayProvider holidayProvider) : IWorkingDayProvider
{
    public async Task<int> GetWorkingDaysAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
        var holidays = (await holidayProvider.GetHolidaysAsync(year, month, cancellationToken)).ToHashSet();
        var count = 0;
        for (var day = 1; day <= DateTime.DaysInMonth(year, month); day++)
        {
            var date = new DateOnly(year, month, day);
            var dow = date.DayOfWeek;
            if (dow is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (holidays.Contains(date)) continue;
            count++;
        }
        return count;
    }
}
