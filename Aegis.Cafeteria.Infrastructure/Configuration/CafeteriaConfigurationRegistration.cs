using System.Net.Http.Json;
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
        IApplicationSecretProvider secrets,
        CafeteriaRuntimeOptions runtime,
        string? siteId,
        CancellationToken cancellationToken = default)
    {
        string? configurationUrl = configuration["Aegis:Configuration:Url"];
        string? registrationKey = await secrets.GetSecretAsync(
            ApplicationSecretNames.ConfigurationRegistrationKey,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(configurationUrl) || string.IsNullOrWhiteSpace(registrationKey))
        {
            return;
        }

        object contract = BuildPosContract(runtime, siteId);

        try
        {
            using HttpClient client = new()
            {
                BaseAddress = new Uri(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(3)
            };
            client.DefaultRequestHeaders.Add("X-Aegis-Registration-Key", registrationKey);

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "api/contracts/register",
                contract,
                cancellationToken);

            // Registration must not become a runtime dependency. A rejected or
            // unavailable registry is intentionally ignored here and can be
            // corrected from the Configuration side without stopping the POS.
            _ = response.IsSuccessStatusCode;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort publication by design.
        }
    }

    private static object BuildPosContract(CafeteriaRuntimeOptions runtime, string? siteId)
    {
        string sqlite = runtime.Persistence.LocalSqliteConnectionString;
        string terminalId = runtime.Persistence.TerminalId;
        string servicesUrl = runtime.Services.BaseUrl;

        return new
        {
            ApplicationId = "Cafeteria",
            DisplayName = "Aegis Cafeteria POS",
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
