namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Security;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Common.Security.Abstractions;
using Common.Security.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;

public sealed class CommonSecurityOperatorAuthenticatorTests
{
    [Fact]
    public async Task VerifiedDirectoryIdentityStartsPendingWithoutAnAutomaticOperatorRole()
    {
        using var fixture = new SecurityAccountFixture();
        var authenticator = new FakeAuthenticator(AuthenticationResult.Succeeded(fixture.Identity()));
        var result = await Create(fixture, authenticator).AuthenticateAsync("fwright", "secret");
        Assert.Null(result);
        Assert.Equal(SecurityAccountStatus.PendingAccess, Assert.Single(fixture.Accounts.List()).Status);
    }

    [Fact]
    public async Task ApprovedOperatorPreservesBusinessAliasAndUsesApplicationManagerPermissions()
    {
        using var fixture = new SecurityAccountFixture();
        VerifiedIdentity identity = fixture.Identity();
        fixture.Approve(identity);
        var options = new OperatorSecurityOptions();
        var roles = new CafeteriaAuthorizationStore(fixture.Factory, options);
        await roles.EnsureInitializedAsync();
        await using (var db = fixture.Factory.CreateDbContext())
            await db.Database.ExecuteSqlRawAsync("INSERT INTO CafeteriaSecurityUserRoles(UserId,RoleKey) VALUES('fwright','Manager')");
        var result = await Create(fixture, new FakeAuthenticator(AuthenticationResult.Succeeded(identity)), options).AuthenticateAsync("FFTPJ\\fwright", "secret");
        Assert.NotNull(result);
        Assert.Equal("fwright", result.UserId);
        Assert.Equal("approved-account", result.AccountId);
        Assert.True(result.IsManager);
    }

    [Fact]
    public async Task ActiveAccountWithoutAnApplicationRoleCannotUseThePos()
    {
        using var fixture = new SecurityAccountFixture();
        var identity = fixture.Identity(); fixture.Approve(identity);
        Assert.Null(await Create(fixture, new FakeAuthenticator(AuthenticationResult.Succeeded(identity))).AuthenticateAsync("fwright", "secret"));
    }

    [Fact]
    public async Task DirectoryManagerGroupCannotBypassPendingAccess()
    {
        using var fixture = new SecurityAccountFixture();
        var identity = fixture.Identity("fwright", "immutable-directory-user", "Cafeteria Managers");
        Assert.Null(await Create(fixture, new FakeAuthenticator(AuthenticationResult.Succeeded(identity)),
            new OperatorSecurityOptions { AdministrativeGroups = ["Cafeteria Managers"] }).AuthenticateAsync("fwright", "secret"));
    }

    [Fact]
    public async Task DisabledAccountCannotAuthenticateEvenWithDirectoryProof()
    {
        using var fixture = new SecurityAccountFixture();
        var identity = fixture.Identity(); var account = fixture.Approve(identity);
        fixture.Accounts.SetStatus(account.Id, SecurityAccountStatus.Disabled, "administrator");
        Assert.Null(await Create(fixture, new FakeAuthenticator(AuthenticationResult.Succeeded(identity))).AuthenticateAsync("fwright", "secret"));
    }

    [Fact]
    public async Task EmptyCredentialsDoNotReachTheDirectoryProvider()
    {
        using var fixture = new SecurityAccountFixture();
        var provider = new FakeAuthenticator(AuthenticationResult.Failed());
        Assert.Null(await Create(fixture, provider).AuthenticateAsync("", "secret"));
        Assert.Equal(0, provider.Calls);
    }

    private static CommonSecurityOperatorAuthenticator Create(SecurityAccountFixture fixture, FakeAuthenticator provider, OperatorSecurityOptions? options = null)
        => new(provider, fixture.Factory, options ?? new(), fixture.Accounts, NullLogger<CommonSecurityOperatorAuthenticator>.Instance);
    private sealed class FakeAuthenticator(AuthenticationResult result) : IAuthenticator
    {
        public int Calls { get; private set; }
        public AuthenticationResult Authenticate(string loginName, string password) { Calls++; return result; }
    }
}
