using System.Text.Json;
using System.Text.Json.Nodes;
using Common.Registration;
using System.Reflection;
using Aegis.Cafeteria.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Aegis.Cafeteria.Infrastructure.Configuration;

/// <summary>
/// Publishes the POS configuration contract to Aegis.Configuration.
/// Registration is observational and best-effort: the POS never depends on
/// Aegis.Configuration being available in order to start or operate.
/// </summary>
public static class CafeteriaConfigurationRegistration
{
    public static async Task TryPublishPosAsync(
        IConfiguration configuration,
        CafeteriaRuntimeOptions runtime,
        string? siteId,
        string applicationId = "Aegis.Cafeteria.POS",
        string displayName = "Aegis Cafeteria POS",
        CancellationToken cancellationToken = default)
    {
        string? configurationUrl = configuration["Aegis:Configuration:Url"];
        if (string.IsNullOrWhiteSpace(configurationUrl))
            return;

        object rawContract = BuildPosContract(runtime, siteId, applicationId, displayName);
        JsonObject contract = JsonSerializer.SerializeToNode(rawContract)?.AsObject()
            ?? throw new InvalidOperationException("POS configuration contract could not be serialized.");

        try
        {
            string instanceId =
                Clean(runtime.Persistence.TerminalId)
                ?? Environment.MachineName;

            string identityFile =
                configuration["Aegis:Registration:IdentityFile"]
                ?? (OperatingSystem.IsWindows()
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Aegis",
                        "Cafeteria.POS",
                        instanceId,
                        "registration-identity.json")
                    : Path.Combine(
                        "/var/lib/aegis/cafeteria-pos",
                        SanitizePathSegment(instanceId),
                        "registration-identity.json"));

            using HttpClient client = new();
            var registration = new RegistrationLifecycleClient(
                client,
                new RegistrationLifecycleOptions(
                    new Uri(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute),
                    applicationId,
                    instanceId,
                    identityFile,
                    TimeSpan.FromSeconds(10)));

            RegistrationLifecycleStatus status =
                await registration.StepAsync(
                    new JsonObject
                    {
                        ["displayName"] = displayName,
                        ["terminalId"] = instanceId,
                        ["siteId"] = Clean(siteId),
                        ["servicesUrl"] = runtime.Services.BaseUrl,
                        ["hostname"] = Environment.MachineName
                    },
                    cancellationToken);

            if (status.IsRegistered)
                _ = await registration.PublishConfigurationContractAsync(contract, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Registration is observational and must not block POS operation.
        }
    }

    private static object BuildPosContract(CafeteriaRuntimeOptions runtime, string? siteId, string applicationId, string displayName)
    {
        string sqlite = runtime.Persistence.LocalSqliteConnectionString;
        string terminalId = runtime.Persistence.TerminalId;
        string servicesUrl = runtime.Services.BaseUrl;

        return new
        {
            ApplicationId = applicationId,
            DisplayName = displayName,
            Version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
            SiteId = Clean(siteId),
            InstanceId = Clean(terminalId),
            Description = "Local-first cafeteria point-of-sale terminal.",
            Requirements = new object[]
            {
                Requirement("local-sqlite", "Local SQLite Database", "Configuration", true,
                    "Stores the terminal's offline cache and local transaction data.",
                    "Persistence:LocalSqliteConnectionString", !string.IsNullOrWhiteSpace(sqlite), sqlite,
                    "Data Source=/var/lib/aegis-cafeteria/cafeteria.local.db;Cache=Shared"),
                Requirement("cafeteria-services-url", "Cafeteria Services URL", "ExternalService", true,
                    "Identifies the Cafeteria Services endpoint used for central synchronization.",
                    "CafeteriaServices:BaseUrl", !string.IsNullOrWhiteSpace(servicesUrl), servicesUrl,
                    "https://cafeteria-services.example.org"),
                Requirement("terminal-id", "Terminal ID", "Configuration", true,
                    "Uniquely identifies this POS terminal.",
                    "Persistence:TerminalId", !string.IsNullOrWhiteSpace(terminalId), terminalId,
                    "CAFETERIA-POS-01"),
                Requirement("site-id", "Site ID", "Configuration", true,
                    "Identifies the site where this terminal is deployed.",
                    "Deployment:SiteId", !string.IsNullOrWhiteSpace(siteId), Clean(siteId),
                    "JM-KINGSTON")
            }
        };
    }

    private static object Requirement(
        string id,
        string displayName,
        string kind,
        bool required,
        string purpose,
        string configurationKey,
        bool isConfigured,
        string? safeDisplayValue,
        string example)
        => new
        {
            Id = id,
            DisplayName = displayName,
            Kind = kind,
            Required = required,
            Purpose = purpose,
            ConfigurationKey = configurationKey,
            IsConfigured = isConfigured,
            SafeDisplayValue = Clean(safeDisplayValue),
            Example = example,
            Sensitive = false
        };

    private static string? Clean(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
