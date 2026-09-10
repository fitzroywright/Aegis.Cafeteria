namespace Aegis.Cafeteria.Infrastructure.Logging;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

/// <summary>
/// Configures Aegis logging using Microsoft.Extensions.Logging only.
/// </summary>
public static class CafeteriaLogging
{
    public static ILoggingBuilder AddCafeteriaLogging(
        this ILoggingBuilder logging,
        CafeteriaLoggingOptions options,
        string? terminalId = null)
    {
        ArgumentNullException.ThrowIfNull(logging);
        ArgumentNullException.ThrowIfNull(options);

        logging.SetMinimumLevel(LogLevel.Information);
        logging.AddFilter("Microsoft", LogLevel.Warning);
        logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

        CafeteriaLogContext context = CreateContext(options, terminalId);

        if (!string.IsNullOrWhiteSpace(options.LocalLogPath))
            logging.AddProvider(new RollingFileLoggerProvider(options, context));

        if (options.GraylogEnabled)
            logging.AddProvider(new GelfUdpLoggerProvider(options, context));

        return logging;
    }

    public static ILoggerFactory AddCafeteriaProviders(
        this ILoggerFactory loggerFactory,
        CafeteriaLoggingOptions options,
        string? terminalId = null)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(options);

        CafeteriaLogContext context = CreateContext(options, terminalId);

        if (!string.IsNullOrWhiteSpace(options.LocalLogPath))
            loggerFactory.AddProvider(new RollingFileLoggerProvider(options, context));

        if (options.GraylogEnabled)
            loggerFactory.AddProvider(new GelfUdpLoggerProvider(options, context));

        return loggerFactory;
    }

    private static CafeteriaLogContext CreateContext(CafeteriaLoggingOptions options, string? terminalId)
    {
        return new CafeteriaLogContext
        (
              options.ApplicationName
            , terminalId ?? Environment.MachineName
            , Environment.MachineName
        );
    }
}

internal sealed record CafeteriaLogContext(string ApplicationName, string TerminalId, string MachineName);
