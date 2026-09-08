namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;

public sealed class StandardWorkingDayProviderTests
{
    [Fact]
    public async Task ExcludesWeekendsAndSuppliedHolidays()
    {
        var provider = new StandardWorkingDayProvider(new Holidays([new DateOnly(2026, 8, 6)]));
        var days = await provider.GetWorkingDaysAsync(2026, 8);
        Assert.Equal(20, days);
    }

    private sealed class Holidays(IReadOnlyCollection<DateOnly> dates) : IHolidayProvider
    {
        public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult(dates);
    }
}
