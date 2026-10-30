namespace Aegis.Cafeteria.PersistenceTests;
using Aegis.Cafeteria.Infrastructure.Security;

public sealed class OperatorSessionLifecycleTests
{
    [Fact]
    public void OperatorSessionExpiresThroughSharedSessionPolicy()
    {
        using var fixture=new SecurityAccountFixture();var account=fixture.Approve(fixture.Identity());var clock=new Clock();
        var session=new OperatorSession(fixture.Accounts,clock);session.SignIn(fixture.Session(account));Assert.True(session.IsAuthenticated);
        clock.Now=clock.Now.AddHours(8);Assert.False(session.IsAuthenticated);Assert.Null(session.Current);
    }
    private sealed class Clock:TimeProvider {public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
}
