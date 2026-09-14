using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(Aegis.Cafeteria.Services.ConfigurationContractHostingStartup))]

namespace Aegis.Cafeteria.Services;

public sealed class ConfigurationContractHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services => services.AddHostedService<ConfigurationContractPublisher>());
    }
}
