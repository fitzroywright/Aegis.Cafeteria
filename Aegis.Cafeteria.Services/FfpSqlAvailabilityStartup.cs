using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: HostingStartup(typeof(Aegis.Cafeteria.Services.FfpSqlAvailabilityHostingStartup))]

namespace Aegis.Cafeteria.Services;

public sealed class FfpSqlAvailabilityHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddTransient<IStartupFilter, FfpSqlAvailabilityStartupFilter>();
        });
    }
}
