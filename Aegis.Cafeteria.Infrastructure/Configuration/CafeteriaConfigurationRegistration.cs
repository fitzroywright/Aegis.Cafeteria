using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using Aegis.Cafeteria.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Aegis.Cafeteria.Infrastructure.Configuration;

/// <summary>
/// Publishes the POS configuration, dependency and runtime-detected environment contracts to
/// Aegis.Configuration. Registration is observational and best-effort; the POS never depends on
/// Aegis.Configuration being available in order to start or operate.
/// Secret values are never included in registration payloads.
/// </summary>
public static class CafeteriaConfigurationRegistration
{
    private const string ApplicationId = "Aegis.Cafeteria.POS";

    public static async Task TryPublishPosAsync(
        IConfiguration configuration,
        IApplicationSecretProvider secrets,
        CafeteriaRuntimeOptions runtime,
        string? siteId,
        CancellationToken cancellationToken = default)
    {
        string? configurationUrl = First(configuration["Aegis:Configuration:Url"], configuration["AegisConfiguration:BaseUrl"]);
        string? registrationKey = await secrets.GetSecretAsync(ApplicationSecretNames.ConfigurationRegistrationKey, cancellationToken);
        if (string.IsNullOrWhiteSpace(configurationUrl) || string.IsNullOrWhiteSpace(registrationKey)) return;

        try
        {
            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            string terminalId = runtime.Persistence.TerminalId;
            object[] requirements = await BuildRequirementsAsync(configuration, secrets, runtime, siteId, cancellationToken);
            object[] dependencies = await BuildDependenciesAsync(configuration, runtime, cancellationToken);
            object detected = await DetectEnvironmentAsync(configuration, runtime, cancellationToken);

            object registration = new
            {
                configuration = new
                {
                    applicationId = ApplicationId,
                    displayName = "Aegis Cafeteria POS",
                    version,
                    siteId = Clean(siteId),
                    instanceId = Clean(terminalId),
                    description = "Local-first cafeteria point-of-sale terminal.",
                    requirements
                },
                dependencies = new
                {
                    applicationId = ApplicationId,
                    version,
                    siteId = Clean(siteId),
                    instanceId = Clean(terminalId),
                    dependencies
                },
                environment = new
                {
                    applicationId = ApplicationId,
                    version,
                    siteId = Clean(siteId),
                    instanceId = Clean(terminalId),
                    environment = detected
                }
            };

            using HttpClient client = new()
            {
                BaseAddress = new Uri(configurationUrl.TrimEnd('/') + "/", UriKind.Absolute),
                Timeout = TimeSpan.FromSeconds(5)
            };
            client.DefaultRequestHeaders.Add("X-Aegis-Registration-Key", registrationKey);
            using HttpResponseMessage response = await client.PostAsJsonAsync("api/registrations/register", registration, cancellationToken);
            _ = response.IsSuccessStatusCode;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Best-effort publication by design.
        }
    }

    private static async Task<object[]> BuildRequirementsAsync(
        IConfiguration configuration,
        IApplicationSecretProvider secrets,
        CafeteriaRuntimeOptions runtime,
        string? siteId,
        CancellationToken ct)
    {
        bool serviceApiKey = !string.IsNullOrWhiteSpace(await secrets.GetSecretAsync(ApplicationSecretNames.CafeteriaServicesApiKey, ct));
        bool messagingToken = runtime.Messaging.Enabled && !string.IsNullOrWhiteSpace(await secrets.GetSecretAsync(ApplicationSecretNames.MessagingBearerToken, ct));
        bool directFfpSecret = !runtime.Services.Enabled && runtime.FfpIntegration.Enabled
            && !string.IsNullOrWhiteSpace(await secrets.GetSecretAsync(ApplicationSecretNames.FfpDatabaseConnectionString, ct));

        List<object> items =
        [
            Config(configuration, "configuration-url", "Aegis:Configuration:Url", "System Configuration URL", false, "Aegis.Configuration endpoint used for observational registration."),
            Config(configuration, "diagnostics-url", "Aegis:Diagnostics:Url", "System Diagnostics URL", false, "Aegis.Diagnostics endpoint when configured."),
            Config(configuration, "site-id", "Deployment:SiteId", "Site ID", true, "Identifies the site where this terminal is deployed.", siteId),
            Config(configuration, "local-sqlite", "Persistence:LocalSqliteConnectionString", "Local SQLite Database", true, "Stores the POS offline cache and local transaction data.", runtime.Persistence.LocalSqliteConnectionString, "Data Source=cafeteria.local.db;Cache=Shared"),
            Config(configuration, "terminal-id", "Persistence:TerminalId", "Terminal ID", true, "Uniquely identifies this POS terminal.", runtime.Persistence.TerminalId, Environment.MachineName),
            Config(configuration, "publication-batch", "Persistence:FfpPublicationBatchSize", "FFP Publication Batch Size", false, "Maximum queued FFP publications processed per batch.", runtime.Persistence.FfpPublicationBatchSize.ToString(), "100"),
            Config(configuration, "poison-limit", "Persistence:FfpPoisonMessageAttemptLimit", "FFP Poison Message Attempt Limit", false, "Retry limit for failed queued publications.", runtime.Persistence.FfpPoisonMessageAttemptLimit.ToString(), "25"),
            Config(configuration, "ffp-enabled", "FfpIntegration:Enabled", "FFP Integration Enabled", false, "Enables FFP reference/publication integration.", runtime.FfpIntegration.Enabled.ToString(), "true"),
            Config(configuration, "ffp-allow-writes", "FfpIntegration:AllowWrites", "FFP Writes Enabled", false, "Allows the selected FFP integration path to publish writes.", runtime.FfpIntegration.AllowWrites.ToString(), "true"),
            Config(configuration, "ffp-refresh", "FfpIntegration:RefreshIntervalMinutes", "FFP Refresh Interval", false, "Reference-data refresh cadence in minutes.", runtime.FfpIntegration.RefreshIntervalMinutes.ToString(), "10"),
            Config(configuration, "ffp-timeout", "FfpIntegration:PublicationTimeoutSeconds", "FFP Publication Timeout", false, "Maximum FFP publication timeout in seconds.", runtime.FfpIntegration.PublicationTimeoutSeconds.ToString(), "120"),
            Config(configuration, "credit-code", "FfpIntegration:CreditLimitLunchItemCode", "Credit Limit Lunch Item Code", false, "FFP product code used for the credit-limit rate.", runtime.FfpIntegration.CreditLimitLunchItemCode, "FFP00"),
            Config(configuration, "subsidy-code", "FfpIntegration:SubsidyLunchItemCode", "Subsidy Lunch Item Code", false, "FFP product code used for the company subsidy rate.", runtime.FfpIntegration.SubsidyLunchItemCode, "FFP00"),
            Config(configuration, "monthly-scan", "FfpIntegration:MonthlyAllocationScanType", "Monthly Allocation Scan Type", false, "FFP transaction mapping for monthly allocations.", runtime.FfpIntegration.MonthlyAllocationScanType.ToString(), "3"),
            Config(configuration, "purchase-scan", "FfpIntegration:PurchaseScanType", "Purchase Scan Type", false, "FFP transaction mapping for purchases.", runtime.FfpIntegration.PurchaseScanType.ToString(), "8"),
            Config(configuration, "stipend-scan", "FfpIntegration:StipendScanType", "Stipend Scan Type", false, "FFP transaction mapping for stipends.", runtime.FfpIntegration.StipendScanType.ToString(), "4"),
            Config(configuration, "refund-scan", "FfpIntegration:RefundScanType", "Refund Scan Type", false, "FFP transaction mapping for refunds.", runtime.FfpIntegration.RefundScanType.ToString(), "9"),
            Config(configuration, "adjustment-scan", "FfpIntegration:AdjustmentScanType", "Adjustment Scan Type", false, "FFP transaction mapping for adjustments.", runtime.FfpIntegration.AdjustmentScanType.ToString(), "9"),
            Config(configuration, "logging-name", "Logging:ApplicationName", "Logging Application Name", false, "Application name attached to logs.", runtime.Logging.ApplicationName, "Aegis.Cafeteria.POS"),
            Config(configuration, "graylog-enabled", "Logging:GraylogEnabled", "Graylog Enabled", false, "Enables Graylog/GELF logging.", runtime.Logging.GraylogEnabled.ToString(), "true"),
            Config(configuration, "graylog-host", "Logging:GraylogHost", "Graylog Host", false, "Graylog server address.", runtime.Logging.GraylogHost, "10.10.0.3", "Logging:GraylogEnabled=true"),
            Config(configuration, "graylog-port", "Logging:GraylogPort", "Graylog Port", false, "Graylog input port.", runtime.Logging.GraylogPort.ToString(), "12201", "Logging:GraylogEnabled=true"),
            Config(configuration, "local-log", "Logging:LocalLogPath", "Local Log Path", false, "Optional local log file path.", runtime.Logging.LocalLogPath),
            Config(configuration, "log-retention", "Logging:RetainedFileCountLimit", "Local Log Retention", false, "Number of retained local log files.", runtime.Logging.RetainedFileCountLimit.ToString(), "31"),
            Config(configuration, "health-enabled", "Health:Enabled", "Health Monitoring Enabled", false, "Enables POS heartbeat/health publishing.", runtime.Health.Enabled.ToString(), "true"),
            Config(configuration, "health-interval", "Health:IntervalSeconds", "Health Interval", false, "Health check cadence in seconds.", runtime.Health.IntervalSeconds.ToString(), "60"),
            Config(configuration, "heartbeat-path", "Health:HeartbeatPath", "Heartbeat Path", false, "Filesystem path written by local heartbeat monitoring.", runtime.Health.HeartbeatPath, @"C:\ProgramData\Aegis.Cafeteria\Health\heartbeat.json", "Health:Enabled=true"),
            Config(configuration, "graylog-heartbeat", "Health:GraylogHeartbeatEveryMinutes", "Graylog Heartbeat Interval", false, "Graylog heartbeat cadence in minutes.", runtime.Health.GraylogHeartbeatEveryMinutes.ToString(), "5", "Health:Enabled=true && Logging:GraylogEnabled=true"),
            Config(configuration, "cache-warning", "Health:EmployeeCacheWarningMinutes", "Employee Cache Warning Age", false, "Cache age that raises a warning.", runtime.Health.EmployeeCacheWarningMinutes.ToString(), "120"),
            Config(configuration, "cache-critical", "Health:EmployeeCacheCriticalMinutes", "Employee Cache Critical Age", false, "Cache age that raises a critical status.", runtime.Health.EmployeeCacheCriticalMinutes.ToString(), "480"),
            Config(configuration, "manager-auth", "Security:ManagerAuthorizationMinutes", "Manager Authorization Minutes", false, "Duration of a successful manager authorization.", configuration["Security:ManagerAuthorizationMinutes"], "2"),
            Config(configuration, "ui-inactivity", "Ui:CustomerInactivityTimeoutSeconds", "Customer Inactivity Timeout", false, "Seconds before the current customer is cleared.", configuration["Ui:CustomerInactivityTimeoutSeconds"], "30"),
            Config(configuration, "ui-recent", "Ui:RecentTransactionCount", "Recent Transaction Count", false, "Number of recent transactions shown in the UI.", configuration["Ui:RecentTransactionCount"], "10"),
            Config(configuration, "ui-tools", "Ui:ShowFfpDataTools", "Show FFP Data Tools", false, "Shows FFP data-maintenance tools.", configuration["Ui:ShowFfpDataTools"], "true"),
            Config(configuration, "services-enabled", "CafeteriaServices:Enabled", "Cafeteria Services Enabled", false, "Enables central Cafeteria Services integration.", runtime.Services.Enabled.ToString(), "false"),
            Config(configuration, "services-url", "CafeteriaServices:BaseUrl", "Cafeteria Services URL", runtime.Services.Enabled, "Central Cafeteria Services API endpoint.", runtime.Services.BaseUrl, "http://localhost:5088", "CafeteriaServices:Enabled=true"),
            Config(configuration, "services-poll", "CafeteriaServices:PollSeconds", "Services Poll Interval", false, "Durable service polling cadence.", runtime.Services.PollSeconds.ToString(), "15", "CafeteriaServices:Enabled=true"),
            Config(configuration, "services-timeout", "CafeteriaServices:RequestTimeoutSeconds", "Services Request Timeout", false, "Cafeteria Services request timeout.", runtime.Services.RequestTimeoutSeconds.ToString(), "10", "CafeteriaServices:Enabled=true"),
            Config(configuration, "startup-refresh-timeout", "CafeteriaServices:StartupRefreshTimeoutSeconds", "Startup Refresh Timeout", false, "Startup employee/holiday refresh timeout.", runtime.Services.StartupRefreshTimeoutSeconds.ToString(), "12", "CafeteriaServices:Enabled=true"),
            Config(configuration, "service-allocations", "CafeteriaServices:UseServiceMonthlyAllocations", "Use Service Monthly Allocations", false, "Uses central Services as the allocation source.", runtime.Services.UseServiceMonthlyAllocations.ToString(), "false", "CafeteriaServices:Enabled=true"),
            Config(configuration, "realtime-enabled", "CafeteriaServices:RealtimeEnabled", "Realtime Enabled", false, "Enables SignalR change notifications.", runtime.Services.RealtimeEnabled.ToString(), "true", "CafeteriaServices:Enabled=true"),
            Config(configuration, "hub-path", "CafeteriaServices:HubPath", "Realtime Hub Path", false, "SignalR hub path.", runtime.Services.HubPath, "/hubs/cafeteria", "CafeteriaServices:RealtimeEnabled=true"),
            Config(configuration, "reconnect", "CafeteriaServices:RealtimeReconnectSeconds", "Realtime Reconnect Interval", false, "SignalR reconnect interval.", runtime.Services.RealtimeReconnectSeconds.ToString(), "5", "CafeteriaServices:RealtimeEnabled=true"),
            Secret("services-api-key", ApplicationSecretNames.CafeteriaServicesApiKey, "Cafeteria Services API Key", runtime.Services.Enabled, "Machine credential used for Cafeteria Services where the configured authentication path requires it.", serviceApiKey, ApplicationSecretNames.CafeteriaServicesApiKey, "CafeteriaServices:Enabled=true"),
            Config(configuration, "messaging-enabled", "Messaging:Enabled", "Messaging Enabled", false, "Enables Common.Messaging remote delivery.", runtime.Messaging.Enabled.ToString(), "false"),
            Config(configuration, "messaging-endpoint", "Messaging:Endpoint", "Messaging Endpoint", false, "Common.Messaging endpoint.", runtime.Messaging.Endpoint, null, "Messaging:Enabled=true"),
            Config(configuration, "messaging-day", "Messaging:WeeklySummaryDay", "Weekly Summary Day", false, "Weekly summary delivery day.", runtime.Messaging.WeeklySummaryDay.ToString(), "Monday", "Messaging:Enabled=true"),
            Config(configuration, "messaging-hour", "Messaging:WeeklySummaryHourLocal", "Weekly Summary Hour", false, "Weekly summary local hour.", runtime.Messaging.WeeklySummaryHourLocal.ToString(), "8", "Messaging:Enabled=true"),
            Secret("messaging-token", ApplicationSecretNames.MessagingBearerToken, "Messaging Bearer Token", runtime.Messaging.Enabled, "Authentication token for enabled remote messaging.", messagingToken, ApplicationSecretNames.MessagingBearerToken, "Messaging:Enabled=true"),
            Secret("direct-ffp-db", ApplicationSecretNames.FfpDatabaseConnectionString, "Direct FFP Database Connection", !runtime.Services.Enabled && runtime.FfpIntegration.Enabled, "Legacy/direct FFP database connection secret used only when the POS is configured for direct FFP access.", directFfpSecret, ApplicationSecretNames.FfpDatabaseConnectionString, "FfpIntegration:Enabled=true && CafeteriaServices:Enabled=false"),
            Secret("registration-key", ApplicationSecretNames.ConfigurationRegistrationKey, "Configuration Registration Credential", false, "Credential used only to publish this observational contract.", true, ApplicationSecretNames.ConfigurationRegistrationKey),
            Secret("secret-zero", "Secret0", "OpenBao Secret Zero", false, "Bootstrap material used by the selected secret provider. Value is never published.", SecretZeroDetected(), "OpenBao/bootstrap")
        ];
        return items.ToArray();
    }

    private static async Task<object[]> BuildDependenciesAsync(IConfiguration configuration, CafeteriaRuntimeOptions runtime, CancellationToken ct)
    {
        List<object> dependencies =
        [
            Dependency("local-sqlite", "POS Local Database", true, "Startup", "Stores offline cache, transactions and synchronization queues.", "POS cannot initialize its local-first persistence layer.", "Microsoft.Data.Sqlite / Entity Framework Core", "SQLite", runtime.Persistence.LocalSqliteConnectionString, null),
            Dependency("configuration", "Aegis.Configuration", false, "Feature", "Receives POS operational contracts.", "POS remains operational; centralized Configuration visibility is unavailable.", "HTTP/HTTPS JSON", "Aegis.Configuration", configuration["Aegis:Configuration:Url"], await ReachableHttpAsync(configuration["Aegis:Configuration:Url"], ct)),
            Dependency("diagnostics", "Aegis.Diagnostics", false, "Feature", "Central diagnostics visibility when configured.", "POS continues with local diagnostics/health behavior.", "HTTP/HTTPS JSON", "Aegis.Diagnostics", configuration["Aegis:Diagnostics:Url"], await ReachableHttpAsync(configuration["Aegis:Diagnostics:Url"], ct))
        ];
        if (runtime.Services.Enabled)
        {
            dependencies.Add(Dependency("cafeteria-services", "Aegis Cafeteria Services", true, "Feature", "Provides central authentication, reference data, synchronization and management integration.", "The POS can continue from its local cache/offline store, but central synchronization, fresh reference data and service-backed functions are degraded.", "HTTP/HTTPS JSON + SignalR", "Aegis.Cafeteria.Services", runtime.Services.BaseUrl, await ReachableHttpAsync(runtime.Services.BaseUrl, ct)));
        }
        else if (runtime.FfpIntegration.Enabled)
        {
            dependencies.Add(Dependency("ffp-database", "FFP Database", true, "Feature", "Direct legacy FFP integration when Cafeteria Services is disabled.", "Local POS operation continues but direct FFP refresh/publication fails.", "Database provider selected by Cafeteria infrastructure", "Declared by configured direct-provider path; not inferred from connection string", null, null));
        }
        if (runtime.Logging.GraylogEnabled)
            dependencies.Add(Dependency("graylog", "Graylog", false, "Feature", "Central structured log collection.", "POS remains operational; central logs/heartbeats are unavailable.", "GELF/UDP logging provider", "Graylog", $"{runtime.Logging.GraylogHost}:{runtime.Logging.GraylogPort}", null));
        if (runtime.Messaging.Enabled)
            dependencies.Add(Dependency("messaging", "Common.Messaging Endpoint", false, "Feature", "Remote weekly-summary messaging.", "POS remains operational; remote messages are not delivered until the endpoint recovers.", "HTTP/HTTPS bearer-token API", "Common.Messaging service", runtime.Messaging.Endpoint, await ReachableHttpAsync(runtime.Messaging.Endpoint, ct)));
        return dependencies.ToArray();
    }

    private static async Task<object> DetectEnvironmentAsync(IConfiguration configuration, CafeteriaRuntimeOptions runtime, CancellationToken ct)
    {
        Process process = Process.GetCurrentProcess();
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        List<object> paths = [];
        List<object> disks = [];
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in RelevantPaths(runtime).Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string path;
            try { path = ResolvePathFromConfiguration(raw); } catch { continue; }
            bool exists = Directory.Exists(path) || File.Exists(path);
            string directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
            bool? writable = Directory.Exists(directory) ? await IsWritableAsync(directory, ct) : null;
            paths.Add(new { path, exists, writable });
            try
            {
                string root = Path.GetPathRoot(path) ?? string.Empty;
                if (root.Length > 0 && roots.Add(root))
                {
                    DriveInfo drive = new(root);
                    if (drive.IsReady) disks.Add(new { path = root, freeBytes = drive.AvailableFreeSpace, totalBytes = drive.TotalSize });
                }
            }
            catch { }
        }

        List<object> endpoints = [];
        foreach ((string name, string? endpoint) in new[]
        {
            ("Aegis.Configuration", configuration["Aegis:Configuration:Url"]),
            ("Aegis.Diagnostics", configuration["Aegis:Diagnostics:Url"]),
            ("Aegis.Cafeteria.Services", runtime.Services.Enabled ? runtime.Services.BaseUrl : null),
            ("Common.Messaging", runtime.Messaging.Enabled ? runtime.Messaging.Endpoint : null)
        })
        {
            if (!string.IsNullOrWhiteSpace(endpoint)) endpoints.Add(new { name, endpoint, reachable = await ReachableHttpAsync(endpoint, ct) });
        }

        return new
        {
            operatingSystem = RuntimeInformation.OSDescription,
            operatingSystemVersion = Environment.OSVersion.VersionString,
            cpuArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            processorCount = Environment.ProcessorCount,
            availableMemoryBytes = memory.TotalAvailableMemoryBytes > 0 ? memory.TotalAvailableMemoryBytes : null,
            processWorkingSetBytes = process.WorkingSet64,
            runtimeVersion = RuntimeInformation.FrameworkDescription,
            networkAvailable = NetworkInterface.GetIsNetworkAvailable(),
            disks,
            paths,
            endpoints,
            hardware = Array.Empty<object>(),
            detectedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static IEnumerable<string> RelevantPaths(CafeteriaRuntimeOptions runtime)
    {
        yield return runtime.Persistence.LocalSqliteConnectionString;
        if (!string.IsNullOrWhiteSpace(runtime.Logging.LocalLogPath)) yield return runtime.Logging.LocalLogPath;
        if (runtime.Health.Enabled && !string.IsNullOrWhiteSpace(runtime.Health.HeartbeatPath)) yield return runtime.Health.HeartbeatPath;
    }

    private static string ResolvePathFromConfiguration(string value)
    {
        if (value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            Microsoft.Data.Sqlite.SqliteConnectionStringBuilder builder = new(value);
            return Path.GetFullPath(builder.DataSource);
        }
        return Path.GetFullPath(value);
    }

    private static object Config(IConfiguration configuration, string id, string key, string displayName, bool required, string purpose, string? effectiveValue = null, string? defaultValue = null, string? conditionalOn = null)
    {
        string environmentKey = key.Replace(":", "__", StringComparison.Ordinal);
        string? env = Environment.GetEnvironmentVariable(environmentKey);
        string? configured = configuration[key];
        string? value = First(env, configured, effectiveValue, defaultValue);
        string source = !string.IsNullOrWhiteSpace(env) ? $"Environment Variable ({environmentKey})" : !string.IsNullOrWhiteSpace(configured) ? "Configuration Provider" : !string.IsNullOrWhiteSpace(effectiveValue) && effectiveValue != defaultValue ? "Resolved Runtime Value" : defaultValue is not null ? "Code Default" : "Unresolved";
        return new
        {
            id,
            displayName,
            kind = "Configuration",
            required,
            purpose,
            configurationKey = key,
            isConfigured = !string.IsNullOrWhiteSpace(value),
            safeDisplayValue = value,
            sensitive = false,
            conditionalOn,
            allowedSources = new[] { "Environment Variable", "appsettings.{Environment}.json", "appsettings.json", "Code Default" },
            resolutionOrder = new[] { "Environment Variable", "appsettings.{Environment}.json", "appsettings.json", "Code Default" },
            effectiveSource = source,
            hasDefault = defaultValue is not null,
            defaultValue,
            redacted = false
        };
    }

    private static object Secret(string id, string key, string displayName, bool required, string purpose, bool configured, string secretPath, string? conditionalOn = null) => new
    {
        id,
        displayName,
        kind = "Secret",
        required,
        purpose,
        configurationKey = key,
        isConfigured = configured,
        safeDisplayValue = (string?)null,
        sensitive = true,
        conditionalOn,
        allowedSources = new[] { "OpenBao/Common.Secrets", "approved Environment Variable override", "Secret0/bootstrap" },
        resolutionOrder = new[] { "secure secret provider", "approved Environment Variable override", "no plaintext fallback" },
        effectiveSource = configured ? "Secure runtime source" : "Unresolved",
        hasDefault = false,
        defaultValue = (string?)null,
        secretPath,
        redacted = true
    };

    private static object Dependency(string id, string displayName, bool required, string criticality, string purpose, string degradedBehavior, string @interface, string? engine, string? endpoint, bool? reachable) => new
    {
        id,
        displayName,
        required,
        criticality,
        purpose,
        degradedBehavior,
        @interface,
        engine,
        endpoint,
        reachable,
        evidence = "Explicitly declared from Cafeteria code; database engines are not inferred from connection strings."
    };

    private static bool SecretZeroDetected() => new[] { "OPENBAO_SECRET_ID", "OPENBAO_TOKEN", "VAULT_TOKEN", "BWS_ACCESS_TOKEN" }.Any(name => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)));
    private static async Task<bool?> ReachableHttpAsync(string? endpoint, CancellationToken ct)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        try { using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(3) }; using HttpResponseMessage _ = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, uri), ct); return true; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }
    private static async Task<bool> IsWritableAsync(string path, CancellationToken ct)
    {
        string probe = Path.Combine(path, $".aegis-write-{Guid.NewGuid():N}");
        try { await File.WriteAllTextAsync(probe, string.Empty, ct); File.Delete(probe); return true; }
        catch { try { if (File.Exists(probe)) File.Delete(probe); } catch { } return false; }
    }
    private static string? First(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
