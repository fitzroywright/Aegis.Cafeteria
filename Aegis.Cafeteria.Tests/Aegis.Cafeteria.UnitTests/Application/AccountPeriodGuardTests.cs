namespace Aegis.Cafeteria.UnitTests.Application;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Domain.Accounts;

public sealed class AccountPeriodGuardTests
{
    [Fact]
    public void ClosedPeriod_RejectsOrdinaryMutation()
    {
        var period = new AccountPeriod(2026, 8, 22);
        var close = new AccountPeriodClose(Guid.NewGuid(), 1, period, 0m, "cafeteria.manager", new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero));
        Assert.Throws<InvalidOperationException>(() => new AccountPeriodGuard().EnsureOpen(period, close));
    }
}
