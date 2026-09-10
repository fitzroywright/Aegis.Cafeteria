namespace Aegis.Cafeteria.UnitTests.Accounts;

using Aegis.Cafeteria.Domain.Accounts;

public sealed class PriorPeriodRefundCreditTests
{
    [Fact]
    public void Constructor_RequiresAppliedPeriodAfterSourcePeriod()
    {
        Assert.Throws<ArgumentException>(() => new PriorPeriodRefundCredit(Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), 2026, 8, 2026, 8, 650m, "manager", DateTimeOffset.UtcNow, "Late refund"));
    }
}
