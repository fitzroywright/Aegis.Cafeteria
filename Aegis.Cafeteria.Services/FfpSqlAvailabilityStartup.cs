using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using System.ComponentModel;

[assembly: HostingStartup(typeof(Aegis.Cafeteria.Services.FfpSqlAvailabilityHostingStartup))]

namespace Aegis.Cafeteria.Services;

/// <summary>
/// Adds outer HTTP pipeline guards for service availability and authenticated management sessions.
/// </summary>
public sealed class FfpSqlAvailabilityHostingStartup : IHostingStartup
{
    public void Configure(IWebHostBuilder builder)
    {
        builder.ConfigureServices
        (
            services =>
            {
                services.AddSingleton<ManagementCommonSecurityAuthenticator>();
                services.AddSingleton<ManagementServiceTokenStore>();
                services.AddTransient<IStartupFilter, ManagementCommonSecurityStartupFilter>();
                services.AddTransient<IStartupFilter, FfpSqlAvailabilityStartupFilter>();
            }
        );
    }
}

public sealed class FfpSqlAvailabilityStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            application.Use
            (
                async (context, nextMiddleware) =>
                {
                    try
                    {
                        await nextMiddleware();
                    }
                    catch (SqlException exception) when (IsConnectivityFailure(exception))
                    {
                        ILogger logger = context.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Aegis.Cafeteria.Services.FfpSqlAvailability");

                        logger.LogWarning
                        (
                              exception
                            , "FFP SQL is unavailable for {Method} {Path}; returning HTTP 503"
                            , context.Request.Method
                            , context.Request.Path
                        );

                        if (context.Response.HasStarted)
                        {
                            throw;
                        }

                        context.Response.Clear();
                        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;

                        await context.Response.WriteAsJsonAsync
                        (
                            new
                            {
                                  error = "FFP SQL is temporarily unavailable."
                                , offline = true
                                , retryable = true
                            }
                        );
                    }
                }
            );

            next(application);
        };
    }

    internal static bool IsConnectivityFailure(SqlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception.Number is -2 or 53 or 64 or 121 or 233 or 258 or 10053 or 10054 or 10060 or 10061 or 11001)
        {
            return true;
        }

        Exception? innerException = exception.InnerException;
        while (innerException is not null)
        {
            if (innerException is Win32Exception)
            {
                return true;
            }

            innerException = innerException.InnerException;
        }

        return exception.Message.Contains
            (
                  "network-related or instance-specific"
                , StringComparison.OrdinalIgnoreCase
            )
            || exception.Message.Contains
            (
                  "could not open a connection to sql server"
                , StringComparison.OrdinalIgnoreCase
            )
            || exception.Message.Contains
            (
                  "server was not found or was not accessible"
                , StringComparison.OrdinalIgnoreCase
            );
    }
}
