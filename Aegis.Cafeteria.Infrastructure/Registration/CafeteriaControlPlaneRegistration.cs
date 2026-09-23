namespace Aegis.Cafeteria.Infrastructure.Registration;

using System.Reflection;
using System.Text.Json.Nodes;
using Common.Registration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed record CafeteriaControlPlaneRegistrationOptions(
    string ApplicationId,
    string DisplayName,
    string Module,
    string InstanceId,
    string IdentityFilePath,
    string Platform,
    string? SiteId = null);

public static class CafeteriaControlPlaneRegistrationExtensions
{
    public static IServiceCollection AddCafeteriaControlPlaneRegistration(
        this IServiceCollection services,
        CafeteriaControlPlaneRegistrationOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<IHostedService>(serviceProvider =>
            new CafeteriaControlPlaneRegistrationHostedService(
                serviceProvider.GetRequiredService<IConfiguration>(),
                serviceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger($"Registration.{options.ApplicationId}"),
                options));

        return services;
    }
}

internal sealed class CafeteriaControlPlaneRegistrationHostedService(
    IConfiguration configuration,
    ILogger logger,
    CafeteriaControlPlaneRegistrationOptions options) : BackgroundService
{
    private static readonly TimeSpan RegisteredDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan PendingDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string configurationUrl = AegisControlPlaneEndpoints.ResolvePublic(
            configuration,
            AegisControlPlaneService.Configuration,
            logger,
            configurationKey: "Aegis:Configuration:Url");

        using HttpClient client = new();
        var registration = new RegistrationLifecycleClient(
            client,
            new RegistrationLifecycleOptions(
                new Uri(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute),
                options.ApplicationId,
                options.InstanceId,
                options.IdentityFilePath,
                TimeSpan.FromSeconds(Math.Clamp(
                    configuration.GetValue("Aegis:Registration:RequestTimeoutSeconds", 30),
                    5,
                    120))),
            logger: logger);

        while (!stoppingToken.IsCancellationRequested)
        {
            RegistrationLifecycleStatus status;
            try
            {
                status = await registration.StepAsync(BuildMetadata(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "{ApplicationId} could not register with the Aegis Control Plane; the application remains operational and will retry.",
                    options.ApplicationId);

                await DelayAsync(RetryDelay, stoppingToken);
                continue;
            }

            if (status.IsRegistered)
            {
                logger.LogInformation(
                    "{ApplicationId} is registered with the Aegis Control Plane. InstanceId={InstanceId} RegistrationId={RegistrationId}",
                    options.ApplicationId,
                    options.InstanceId,
                    status.RegistrationId);
            }
            else
            {
                logger.LogInformation(
                    "{ApplicationId} registration state is {RegistrationState}. InstanceId={InstanceId} RegistrationId={RegistrationId} Detail={Detail}",
                    options.ApplicationId,
                    status.State,
                    options.InstanceId,
                    status.RegistrationId,
                    status.Error ?? "No additional detail.");
            }

            TimeSpan delay = status.State switch
            {
                RegistrationLifecycleState.Registered => RegisteredDelay,
                RegistrationLifecycleState.Pending or RegistrationLifecycleState.RecoveryPending => PendingDelay,
                RegistrationLifecycleState.IdentityConflict or
                RegistrationLifecycleState.Rejected or
                RegistrationLifecycleState.Revoked => RetryDelay,
                _ => RetryDelay
            };

            await DelayAsync(delay, stoppingToken);
        }
    }

    private JsonObject BuildMetadata()
        => new()
        {
            ["displayName"] = options.DisplayName,
            ["module"] = options.Module,
            ["siteId"] = string.IsNullOrWhiteSpace(options.SiteId) ? null : options.SiteId,
            ["hostname"] = Environment.MachineName,
            ["platform"] = options.Platform,
            ["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            ["runtimeEnvironment"] = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production",
            ["presentation"] = new JsonObject
            {
                ["shortName"] = options.Module switch
                {
                    "Aegis.Cafeteria.POS" => "Cafeteria POS",
                    "Aegis.Cafeteria.POS.UI" => "Cafeteria POS UI",
                    _ => "Cafeteria"
                },
                ["accent"] = "gold"
            }
        };

    private static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}

public static class CafeteriaRegistrationIdentity
{
    public static string BuildInstanceId(
        string terminalId,
        string platform)
    {
        string terminal = string.IsNullOrWhiteSpace(terminalId)
            ? Environment.MachineName
            : terminalId.Trim();
        string platformName = string.IsNullOrWhiteSpace(platform)
            ? "Unknown"
            : platform.Trim();

        return $"{terminal}-{platformName}";
    }

    public static string BuildIdentityFilePath(
        string applicationId,
        string instanceId)
    {
        string safeApplication = Sanitize(applicationId);
        string safeInstance = Sanitize(instanceId);

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Aegis",
                "Cafeteria",
                "Registration",
                safeApplication,
                safeInstance,
                "registration-identity.json");
        }

        return Path.Combine(
            "/var/lib/aegis-cafeteria/registration",
            safeApplication,
            safeInstance,
            "registration-identity.json");
    }

    private static string Sanitize(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        return new string(value.Select(character =>
            invalid.Contains(character) ? '_' : character).ToArray());
    }
}
