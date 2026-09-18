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
        string? operationsUrl = configuration["Aegis:Operations:Url"] ?? configuration["AegisOperations:BaseUrl"];
        if (string.IsNullOrWhiteSpace(operationsUrl))
            return;

        object rawContract = BuildPosContract(runtime, siteId, applicationId, displayName);
        JsonObject contract = JsonSerializer.SerializeToNode(rawContract)?.AsObject()
            ?? throw new InvalidOperationException("POS configuration contract could not be serialized.");

        try
        {
            using HttpClient client = new();
            var registration = new ApplicationRegistrationClient(
                client,
                new ApplicationRegistrationOptions(
                    new Uri(operationsUrl.TrimEnd('/') + "/", UriKind.Absolute),
                    applicationId,
                    runtime.Persistence.TerminalId,
                    RegistrationCredentialResolver.EnvironmentVariableName,
                    TimeSpan.FromSeconds(10)));

            _ = await registration.RegisterAsync(contract, cancellationToken);
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
