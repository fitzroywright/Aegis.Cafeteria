namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class AccountPeriodTests
{
    [Fact]
    public void Contains_ReturnsTrueOnlyForSameMonth()
    {
        var period = new AccountPeriod(2026, 8, 21);

        Assert.True(period.Contains(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.True(period.Contains(new DateTimeOffset(2026, 8, 31, 23, 59, 0, TimeSpan.Zero)));
        Assert.False(period.Contains(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)));
    }
}
