namespace Aegis.Cafeteria.UnitTests.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;

public sealed class StandardWorkingDayProviderTests
{
    [Fact]
    public async Task GetWorkingDaysAsync_WithEmploymentStartDate_ProrationExcludesWeekendAndHoliday()
    {
        DateOnly startDate = new(2026, 9, 14);
        DateOnly holiday = new(2026, 9, 21);
        StandardWorkingDayProvider provider = new(new StubHolidayProvider([holiday]));

        int actual = await provider.GetWorkingDaysAsync(2026, 9, startDate);

        int expected = Enumerable.Range(startDate.Day, DateTime.DaysInMonth(2026, 9) - startDate.Day + 1)
            .Select(day => new DateOnly(2026, 9, day))
            .Count(date => date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && date != holiday);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task GetWorkingDaysAsync_StartAfterMonth_ReturnsZero()
    {
        StandardWorkingDayProvider provider = new(new StubHolidayProvider([]));

        int actual = await provider.GetWorkingDaysAsync(2026, 9, new DateOnly(2026, 10, 1));

        Assert.Equal(0, actual);
    }

    private sealed class StubHolidayProvider(IReadOnlyCollection<DateOnly> holidays) : IHolidayProvider
    {
        public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult(holidays);
    }
}
