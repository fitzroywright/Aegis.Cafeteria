namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Logging;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public sealed class CafeteriaRuntimeStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            IConfiguration configuration = application.ApplicationServices.GetRequiredService<IConfiguration>();
            ILoggerFactory loggerFactory = application.ApplicationServices.GetRequiredService<ILoggerFactory>();
            CafeteriaLoggingOptions loggingOptions = configuration.GetSection("Logging").Get<CafeteriaLoggingOptions>()
                ?? new CafeteriaLoggingOptions();

            loggerFactory.AddCafeteriaProviders(loggingOptions);
            application.UseCafeteriaAuditTrail();

            next(application);
        };
    }
}
