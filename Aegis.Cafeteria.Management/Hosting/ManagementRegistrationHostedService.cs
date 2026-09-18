using System.Reflection;
using System.Text.Json.Nodes;
using Common.Registration;

namespace Aegis.Cafeteria.Management;

internal sealed class ManagementRegistrationHostedService(
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<ManagementRegistrationHostedService> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Management";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? operationsUrl = configuration["Aegis:Operations:Url"] ?? configuration["AegisOperations:BaseUrl"];
        if (string.IsNullOrWhiteSpace(operationsUrl))
        {
            logger.LogInformation("Application registration is disabled because no Operations URL is configured.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryRegisterAsync(operationsUrl, stoppingToken))
                return;

            try { await Task.Delay(RetryDelay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<bool> TryRegisterAsync(string operationsUrl, CancellationToken cancellationToken)
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

            using HttpClient client = new();
            var registration = new ApplicationRegistrationClient(
                client,
                new ApplicationRegistrationOptions(
                    new Uri(operationsUrl.TrimEnd('/') + "/", UriKind.Absolute),
                    ApplicationId,
                    instanceId,
                    RegistrationCredentialResolver.EnvironmentVariableName,
                    TimeSpan.FromSeconds(10)));

            ApplicationRegistrationStatus result = await registration.RegisterAsync(contract, cancellationToken);
            if (!result.IsRegistered)
            {
                logger.LogWarning(
                    "Aegis.Cafeteria.Management registration state is {RegistrationState}: {RegistrationError}; Management remains operational. Bootstrap must be repeated in Operations when the key is missing, invalid, or revoked.",
                    result.State,
                    result.Error ?? "No additional detail.");
                return false;
            }

            logger.LogInformation("Aegis.Cafeteria.Management registration is valid and its configuration contract was published.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Unable to register Aegis.Cafeteria.Management; Management remains operational and will retry.");
            return false;
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
