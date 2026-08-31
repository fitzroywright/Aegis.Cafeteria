namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;

public sealed class AccountPeriodFactoryTests
{
    [Fact]
    public async Task CreateAsync_UsesWorkingDayProvider()
    {
        var factory = new AccountPeriodFactory(new StubWorkingDayProvider(21));

        var period = await factory.CreateAsync(2026, 9);

        Assert.Equal(2026, period.Year);
        Assert.Equal(9, period.Month);
        Assert.Equal(21, period.WorkingDays);
    }

    private sealed class StubWorkingDayProvider(int workingDays) : IWorkingDayProvider
    {
        public Task<int> GetWorkingDaysAsync(int year, int month, CancellationToken cancellationToken = default)
            => Task.FromResult(workingDays);
    }
}
