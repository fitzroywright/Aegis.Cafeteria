namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Security;
using Common.Security.Abstractions;

public sealed class ManagerAuthorizationRegistryTests
{
    [Fact]
    public void GrantManagerAllowsNormalizedIdentityUntilRevoked()
    {
        using var fixture = new SecurityAccountFixture();
        var account = fixture.Approve(fixture.Identity("FFTPJ\\Manager1"));
        var session = new OperatorSession(fixture.Accounts);
        var registry = new ManagerAuthorizationRegistry(session, new OperatorSecurityOptions(), fixture.Accounts);
        registry.Grant(fixture.Session(account));
        Assert.True(registry.IsAuthorized("manager1"));
        Assert.True(registry.IsAuthorized("manager1@fftpj.local"));
        fixture.Accounts.RevokeSessions(account.Id, "administrator");
        Assert.False(registry.IsAuthorized("manager1"));
    }

    [Fact]
    public void NonManagerCannotGrantApproval()
    {
        using var fixture = new SecurityAccountFixture();
        var account = fixture.Approve(fixture.Identity());
        var registry = new ManagerAuthorizationRegistry(new OperatorSession(fixture.Accounts), new OperatorSecurityOptions(), fixture.Accounts);
        Assert.Throws<UnauthorizedAccessException>(() => registry.Grant(fixture.Session(account, manager: false)));
    }

    [Fact]
    public void CurrentManagerSessionIsInvalidatedWhenAccountIsDisabled()
    {
        using var fixture = new SecurityAccountFixture();
        var account = fixture.Approve(fixture.Identity("manager"));
        var session = new OperatorSession(fixture.Accounts);session.SignIn(fixture.Session(account));
        var registry = new ManagerAuthorizationRegistry(session, new OperatorSecurityOptions(), fixture.Accounts);
        Assert.True(registry.IsAuthorized("manager"));
        Assert.False(registry.IsAuthorized("somebody-else"));
        fixture.Accounts.SetStatus(account.Id, SecurityAccountStatus.Disabled, "administrator");
        Assert.False(session.IsAuthenticated);
        Assert.False(registry.IsAuthorized("manager"));
    }
}
