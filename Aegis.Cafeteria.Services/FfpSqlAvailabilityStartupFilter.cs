using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace Aegis.Cafeteria.Services;

public sealed class FfpSqlAvailabilityStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            application.Use(async (context, nextMiddleware) =>
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

                    logger.LogWarning(exception, "FFP SQL is unavailable for {Method} {Path}; returning HTTP 503", context.Request.Method, context.Request.Path);
                    if (context.Response.HasStarted) throw;

                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "FFP SQL is temporarily unavailable.",
                        offline = true,
                        retryable = true
                    });
                }
            });

            next(application);
        };
    }

    internal static bool IsConnectivityFailure(SqlException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception.Number is -2 or 53 or 64 or 121 or 233 or 258 or 10053 or 10054 or 10060 or 10061 or 11001) return true;

        for (Exception? inner = exception.InnerException; inner is not null; inner = inner.InnerException)
            if (inner is Win32Exception) return true;

        return exception.Message.Contains("network-related or instance-specific", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("could not open a connection to sql server", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("server was not found or was not accessible", StringComparison.OrdinalIgnoreCase);
    }
}
