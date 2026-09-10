namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Security;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Security;

public sealed class ManagerAuthorizationRegistryTests
{
    [Fact]
    public void Grant_Manager_AllowsNormalizedIdentity()
    {
        var session = new OperatorSession();
        var registry = new ManagerAuthorizationRegistry(session, new OperatorSecurityOptions { ManagerAuthorizationMinutes = 2 });
        registry.Grant(new OperatorIdentity("FFTPJ\\Manager1", "Manager One", true, DateTimeOffset.UtcNow));

        Assert.True(registry.IsAuthorized("manager1"));
        Assert.True(registry.IsAuthorized("manager1@fftpj.local"));
    }

    [Fact]
    public void Grant_NonManager_Throws()
    {
        var registry = new ManagerAuthorizationRegistry(new OperatorSession(), new OperatorSecurityOptions());
        Assert.Throws<UnauthorizedAccessException>(() =>
            registry.Grant(new OperatorIdentity("cashier", "Cashier", false, DateTimeOffset.UtcNow)));
    }

    [Fact]
    public void CurrentManagerSession_IsAuthorizedWithoutSeparateGrant()
    {
        var session = new OperatorSession();
        session.SignIn(new OperatorIdentity("manager", "Manager", true, DateTimeOffset.UtcNow));
        var registry = new ManagerAuthorizationRegistry(session, new OperatorSecurityOptions());

        Assert.True(registry.IsAuthorized("manager"));
        Assert.False(registry.IsAuthorized("somebody-else"));
    }
}
