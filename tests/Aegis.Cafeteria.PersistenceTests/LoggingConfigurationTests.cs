namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

public sealed class LoggingConfigurationTests
{
    [Fact]
    public void DefaultLoggingTargetsConfiguredGraylogServer()
    {
        var o = new CafeteriaLoggingOptions();
        Assert.Equal("10.10.0.3", o.GraylogHost);
        Assert.Equal(12201, o.GraylogPort);
        Assert.True(o.GraylogEnabled);
    }

    [Fact]
    public void Logging_UsesMicrosoftExtensionsLogging()
    {
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddCafeteriaLogging(
            new CafeteriaLoggingOptions
            {
                GraylogEnabled = false,
                LocalLogPath = Path.Combine(Path.GetTempPath(), $"cafeteria-test-{Guid.NewGuid():N}-.log"),
                RetainedFileCountLimit = 2
            },
            "TEST"));

        using var provider = services.BuildServiceProvider();
        var logger = provider.GetRequiredService<ILogger<LoggingConfigurationTests>>();
        logger.LogInformation("Persistence test log");
        Assert.NotNull(logger);
    }
}
