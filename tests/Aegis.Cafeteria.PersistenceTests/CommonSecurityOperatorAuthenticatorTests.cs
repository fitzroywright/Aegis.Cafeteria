namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Security;
using Common.Security.Abstractions;
using Common.Security.Models;
using Microsoft.Extensions.Logging.Abstractions;

public sealed class CommonSecurityOperatorAuthenticatorTests
{
    [Fact]
    public async Task AuthenticateAsync_Success_NormalizesUserAndPreservesManagerFlag()
    {
        var common = new FakeAuthenticator(new AuthenticationResult
        {
            IsAuthenticated = true,
            IsAdministrative = true,
            UserInfo = new ApplicationUserInfo { DisplayName = "Test Manager" }
        });
        var sut = new CommonSecurityOperatorAuthenticator(common, NullLogger<CommonSecurityOperatorAuthenticator>.Instance);

        var identity = await sut.AuthenticateAsync("FFTPJ\\TManager", "secret");

        Assert.NotNull(identity);
        Assert.Equal("tmanager", identity!.UserId);
        Assert.Equal("Test Manager", identity.DisplayName);
        Assert.True(identity.IsManager);
        Assert.Equal("secret", common.LastPassword);
    }

    [Fact]
    public async Task AuthenticateAsync_AuthenticatedWithoutProfile_FailsClosed()
    {
        var sut = new CommonSecurityOperatorAuthenticator(
            new FakeAuthenticator(new AuthenticationResult { IsAuthenticated = true, IsAdministrative = true, UserInfo = null }),
            NullLogger<CommonSecurityOperatorAuthenticator>.Instance);

        Assert.Null(await sut.AuthenticateAsync("manager", "secret"));
    }

    [Fact]
    public async Task AuthenticateAsync_DoesNotAuthenticateBlankCredentials()
    {
        var common = new FakeAuthenticator(new AuthenticationResult { IsAuthenticated = true, UserInfo = new ApplicationUserInfo() });
        var sut = new CommonSecurityOperatorAuthenticator(common, NullLogger<CommonSecurityOperatorAuthenticator>.Instance);

        Assert.Null(await sut.AuthenticateAsync("", "secret"));
        Assert.Equal(0, common.CallCount);
    }

    private sealed class FakeAuthenticator(AuthenticationResult result) : IAuthenticator
    {
        public int CallCount { get; private set; }
        public string? LastPassword { get; private set; }
        public AuthenticationResult Authenticate(string loginName, string password)
        {
            CallCount++;
            LastPassword = password;
            return result;
        }
    }
}
