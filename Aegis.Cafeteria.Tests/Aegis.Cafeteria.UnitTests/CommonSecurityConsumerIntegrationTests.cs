using Common.Diagnostics;
using Common.Security.Abstractions;
using Common.Security.Integration;
using Common.Security.Options;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Cafeteria.UnitTests;

public sealed class CommonSecurityConsumerIntegrationTests
{
    [Fact]
    public void CafeteriaCanComposeCommonSecurityWithPlatformDiagnostics()
    {
        ServiceCollection services = new();
        services.AddCommonDiagnostics();
        services.AddCommonSecurity(new ActiveDirectoryAuthenticationOptions
        {
            Domain = "fftpj.local",
            SearchBase = "DC=fftpj,DC=local",
            Servers = ["jmad01.fftpj.local", "jmad02.fftpj.local"],
            Port = 636
        });
        services.AddCommonSecurityDiagnostics();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAuthenticator>());
        Assert.Contains(provider.GetServices<IDiagnosticCheck>(), check => check.Name == "Common.Security");
    }
}
