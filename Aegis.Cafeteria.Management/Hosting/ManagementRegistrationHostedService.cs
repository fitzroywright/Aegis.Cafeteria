using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Common.Registration;

namespace Aegis.Cafeteria.Management;

internal sealed class ManagementRegistrationHostedService(
    IConfiguration configuration,
    ILogger<ManagementRegistrationHostedService> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Management";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string configurationUrl = AegisControlPlaneEndpoints.ResolvePublic(
            configuration,
            AegisControlPlaneService.Configuration,
            logger,
            configurationKey: "Aegis:Configuration:Url");

        while (!stoppingToken.IsCancellationRequested)
        {
            RegistrationLifecycleState state =
                await TryRegisterAsync(configurationUrl, stoppingToken);

            TimeSpan delay = state switch
            {
                RegistrationLifecycleState.Registered => TimeSpan.FromMinutes(5),
                RegistrationLifecycleState.Pending or RegistrationLifecycleState.RecoveryPending => TimeSpan.FromSeconds(10),
                RegistrationLifecycleState.IdentityConflict or RegistrationLifecycleState.Rejected or RegistrationLifecycleState.Revoked => TimeSpan.FromMinutes(1),
                _ => RetryDelay
            };

            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<RegistrationLifecycleState> TryRegisterAsync(string configurationUrl, CancellationToken cancellationToken)
    {
        try
        {
            string instanceId = configuration["Service:Identity"] ?? Environment.MachineName;
            string? publicUrl = configuration["Aegis:PublicUrl"];
            string? servicesUrl = configuration["Services:BaseUrl"] ?? configuration["CafeteriaServices:BaseUrl"];

            var contract = new JsonObject
            {
                ["applicationId"] = ApplicationId,
                ["displayName"] = "Aegis Cafeteria Management",
                ["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                ["siteId"] = NullIfBlank(configuration["Deployment:SiteId"] ?? configuration["Site:Id"]),
                ["instanceId"] = instanceId,
                ["description"] = "Independent management web interface for Aegis Cafeteria.",
                ["requirements"] = new JsonArray
                {
                    Requirement("public-url", "Public URL", "Aegis:PublicUrl", true, publicUrl),
                    Requirement("services-url", "Cafeteria Services URL", "Services:BaseUrl", true, servicesUrl)
                }
            };

            string identityFile =
                configuration["Aegis:Registration:IdentityFile"]
                ?? (OperatingSystem.IsWindows()
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Aegis",
                        "Cafeteria.Management",
                        "registration-identity.json")
                    : "/var/lib/aegis/cafeteria-management/registration-identity.json");

            using HttpClient client = new();
            var registration = new RegistrationLifecycleClient(
                client,
                new RegistrationLifecycleOptions(
                    new Uri(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute),
                    ApplicationId,
                    instanceId,
                    identityFile,
                    TimeSpan.FromSeconds(10)),
                logger: logger);

            RegistrationLifecycleStatus result =
                await registration.StepAsync(
                    new JsonObject
                    {
                        ["displayName"] = "Aegis Cafeteria Management",
                        ["publicUrl"] = publicUrl,
                        ["servicesUrl"] = servicesUrl,
                        ["hostname"] = Environment.MachineName
                    },
                    cancellationToken);

            if (!result.IsRegistered)
            {
                logger.LogInformation(
                    "Aegis.Cafeteria.Management registration state is {RegistrationState}: {RegistrationError}; Management remains operational.",
                    result.State,
                    result.Error ?? "No additional detail.");
                return result.State;
            }

            HttpStatusCode contractStatus =
                await registration.PublishConfigurationContractAsync(contract, cancellationToken);

            if ((int)contractStatus is < 200 or >= 300)
            {
                logger.LogWarning(
                    "Aegis.Cafeteria.Management is registered, but Configuration contract publication returned HTTP {StatusCode}.",
                    (int)contractStatus);
            }
            else
            {
                logger.LogInformation(
                    "Aegis.Cafeteria.Management registration is valid and its configuration contract was published.");
            }

            return result.State;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to register Aegis.Cafeteria.Management; Management remains operational and will retry.");
            return RegistrationLifecycleState.Error;
        }
    }

    private static JsonObject Requirement(string id, string displayName, string key, bool required, string? value)
    {
        bool configured = !string.IsNullOrWhiteSpace(value);
        return new JsonObject
        {
            ["id"] = id,
            ["displayName"] = displayName,
            ["kind"] = "Configuration",
            ["required"] = required,
            ["purpose"] = displayName,
            ["configurationKey"] = key,
            ["isConfigured"] = configured,
            ["effectiveValueAvailable"] = configured,
            ["configurationState"] = configured ? "Configured" : "Missing"
        };
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
