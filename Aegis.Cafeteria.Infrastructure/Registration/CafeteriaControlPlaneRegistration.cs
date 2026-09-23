namespace Aegis.Cafeteria.Infrastructure.Registration;

using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Common.Diagnostics;
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

        string operationsUrl = AegisControlPlaneEndpoints.ResolvePublic(
            configuration,
            AegisControlPlaneService.Operations,
            logger,
            configurationKey: "Aegis:Operations:Url");

        using HttpClient client = new();
        using HttpClient lifecycleClient = new();
        ILifecycleEventSink lifecycle = CreateLifecycleSink(
            operationsUrl,
            lifecycleClient);

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
                    120)),
                RunbookReference: "docs/RUNBOOK.md"),
            logger: logger,
            lifecycleEventSink: lifecycle);

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
                HttpStatusCode contractStatus =
                    await registration.PublishConfigurationContractAsync(
                        BuildContract(),
                        stoppingToken);

                if ((int)contractStatus is >= 200 and < 300)
                {
                    logger.LogInformation(
                        "{ApplicationId} is registered with the Aegis Control Plane and its Configuration contract was published. InstanceId={InstanceId} RegistrationId={RegistrationId}",
                        options.ApplicationId,
                        options.InstanceId,
                        status.RegistrationId);
                }
                else
                {
                    logger.LogWarning(
                        "{ApplicationId} is registered, but Configuration contract publication returned HTTP {StatusCode}.",
                        options.ApplicationId,
                        (int)contractStatus);
                }
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

    private JsonObject BuildContract()
    {
        string? servicesUrl =
            configuration["CafeteriaServices:BaseUrl"]
            ?? configuration["Services:BaseUrl"];

        return new JsonObject
        {
            ["applicationId"] = options.ApplicationId,
            ["displayName"] = options.DisplayName,
            ["module"] = options.Module,
            ["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            ["siteId"] = string.IsNullOrWhiteSpace(options.SiteId) ? null : options.SiteId,
            ["instanceId"] = options.InstanceId,
            ["description"] = $"{options.DisplayName} runtime module.",
            ["runbookReference"] = "docs/RUNBOOK.md",
            ["presentation"] = new JsonObject
            {
                ["shortName"] = options.Module switch
                {
                    "Aegis.Cafeteria.POS" => "Cafeteria POS",
                    "Aegis.Cafeteria.POS.UI" => "Cafeteria POS UI",
                    _ => "Cafeteria"
                },
                ["accent"] = "gold"
            },
            ["runtimeEnvironment"] = new JsonObject
            {
                ["machineName"] = Environment.MachineName,
                ["platform"] = options.Platform,
                ["frameworkDescription"] =
                    System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                ["operatingSystem"] =
                    System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                ["processArchitecture"] =
                    System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
            },
            ["diagnostics"] = new JsonObject
            {
                ["supportsRemoteDiagnostics"] = false,
                ["supportsOperationalTelemetry"] = true,
                ["authenticationScheme"] = "None",
                ["supportedLevels"] = new JsonArray()
            },
            ["requirements"] = new JsonArray
            {
                Requirement(
                    "configuration-url",
                    "Aegis Configuration URL",
                    "Aegis:Configuration:Url",
                    configuration["Aegis:Configuration:Url"]),
                Requirement(
                    "operations-url",
                    "Aegis Operations URL",
                    "Aegis:Operations:Url",
                    configuration["Aegis:Operations:Url"]),
                Requirement(
                    "cafeteria-services-url",
                    "Cafeteria Services URL",
                    "CafeteriaServices:BaseUrl",
                    servicesUrl),
                new JsonObject
                {
                    ["id"] = "terminal-id",
                    ["displayName"] = "Terminal Identity",
                    ["kind"] = "Runtime",
                    ["required"] = true,
                    ["purpose"] = "Stable POS terminal identity used to scope the Control Plane instance.",
                    ["isConfigured"] = !string.IsNullOrWhiteSpace(options.InstanceId),
                    ["configurationState"] =
                        !string.IsNullOrWhiteSpace(options.InstanceId) ? "Configured" : "Missing"
                }
            }
        };
    }

    private static JsonObject Requirement(
        string id,
        string displayName,
        string key,
        string? value)
    {
        bool configured = !string.IsNullOrWhiteSpace(value);
        return new JsonObject
        {
            ["id"] = id,
            ["displayName"] = displayName,
            ["kind"] = "Configuration",
            ["required"] = true,
            ["purpose"] = displayName,
            ["configurationKey"] = key,
            ["isConfigured"] = configured,
            ["effectiveValueAvailable"] = configured,
            ["configurationState"] = configured ? "Configured" : "Missing"
        };
    }

    private ILifecycleEventSink CreateLifecycleSink(
        string operationsUrl,
        HttpClient client)
    {
        if (!Uri.TryCreate(
                operationsUrl.TrimEnd('/') + "/api/operations/activity/observe",
                UriKind.Absolute,
                out Uri? endpoint))
            return new NullLifecycleEventSink();

        var store = new FileRegistrationIdentityStore(options.IdentityFilePath);
        return new HttpLifecycleEventSink(
            client,
            new HttpLifecycleEventSinkOptions(endpoint, TimeSpan.FromSeconds(10)),
            async (request, lifecycleEvent, cancellationToken) =>
            {
                RegistrationIdentityDocument identity =
                    await store.LoadOrCreateAsync(
                        options.ApplicationId,
                        options.InstanceId,
                        cancellationToken);

                if (string.IsNullOrWhiteSpace(identity.Credential))
                    return;

                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        identity.Credential);
                request.Headers.TryAddWithoutValidation(
                    "X-Aegis-Application-Id",
                    identity.ApplicationId);
                request.Headers.TryAddWithoutValidation(
                    "X-Aegis-Instance-Id",
                    identity.InstanceId);
                request.Headers.TryAddWithoutValidation(
                    "X-Aegis-Installation-Id",
                    identity.InstallationId);
            });
    }

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
