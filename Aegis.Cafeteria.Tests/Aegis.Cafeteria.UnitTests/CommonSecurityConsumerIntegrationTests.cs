using Common.Diagnostics;
using Common.Security.Abstractions;
using Common.Security.Integration;
using Common.Security.Plugin.ActiveDirectory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aegis.Cafeteria.UnitTests;

public sealed class CommonSecurityConsumerIntegrationTests
{
    [Fact]
    public void CafeteriaCanComposeCommonSecurityWithPlatformDiagnostics()
    {
        ServiceCollection services = new();
        services.AddCommonDiagnostics();
        services.AddCommonSecurity();

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:ActiveDirectory:Domain"] = "fftpj.local",
                ["Authentication:ActiveDirectory:SearchBase"] = "DC=fftpj,DC=local",
                ["Authentication:ActiveDirectory:Servers:0"] = "jmad01.fftpj.local",
                ["Authentication:ActiveDirectory:Servers:1"] = "jmad02.fftpj.local",
                ["Authentication:ActiveDirectory:Port"] = "636"
            })
            .Build();

        new ActiveDirectoryAuthenticationPlugin()
            .Register(services, configuration.GetSection("Authentication:ActiveDirectory"));
        services.AddCommonSecurityDiagnostics();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAuthenticator>());
        Assert.Contains(provider.GetServices<IDiagnosticCheck>(), check => check.Name == "Common.Security");
    }
}
