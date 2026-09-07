using Aegis.Cafeteria.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(CafeteriaAuthenticationHostingStartup))]

namespace Aegis.Cafeteria.Services;

public sealed class CafeteriaAuthenticationHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices
        (
            services =>
            {
                services.AddSingleton<ManagementCommonSecurityAuthenticator>();
                services.AddSingleton<ManagementServiceTokenStore>();
                services.AddSingleton<CafeteriaRoleResolver>();
                services.AddSingleton<IStartupFilter, ManagementCommonSecurityStartupFilter>();
                services.AddSingleton<IStartupFilter, CafeteriaRuntimeStartupFilter>();
            }
        );
    }
}
