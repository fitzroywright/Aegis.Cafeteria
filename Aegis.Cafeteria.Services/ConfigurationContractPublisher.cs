using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Common.Secrets;

namespace Aegis.Cafeteria.Services;

public sealed class ConfigurationContractPublisher(IConfiguration configuration, IWebHostEnvironment environment, ILogger<ConfigurationContractPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Services";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? baseUrl = First(configuration["Aegis:Configuration:Url"], configuration["AegisConfiguration:BaseUrl"]);
        if (string.IsNullOrWhiteSpace(baseUrl)) { logger.LogInformation("Aegis.Configuration publication is disabled because no Configuration URL is configured."); return; }
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryPublishAsync(baseUrl, stoppingToken)) return;
            try { await Task.Delay(RetryDelay, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<bool> TryPublishAsync(string baseUrl, CancellationToken ct)
    {
        try
        {
            ISecretProvider secrets = CommonSecretProviderFactory.Create(configuration);
            string? key = await secrets.GetAsync($"configuration/registration/{ApplicationId}", ct);
            if (string.IsNullOrWhiteSpace(key)) { logger.LogWarning("Aegis.Configuration registration credential is unavailable; Cafeteria Services remains operational and will retry publication."); return false; }

            string version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
            string? siteId = First(configuration["Deployment:SiteId"], configuration["Site:Id"]);
            string instanceId = First(configuration["Service:Identity"], Environment.MachineName) ?? Environment.MachineName;
            JsonArray requirements = BuildRequirements();
            List<object> dependencies = await BuildDependenciesAsync(baseUrl, ct);
            object detected = await DetectEnvironmentAsync(baseUrl, ct);

            object registration = new
            {
                configuration = new
                {
                    applicationId = ApplicationId,
                    displayName = "Aegis Cafeteria Services",
                    version,
                    siteId,
                    instanceId,
                    description = "Central synchronization, management, reporting, authentication and FFP integration services for Aegis Cafeteria.",
                    requirements
                },
                dependencies = new { applicationId = ApplicationId, version, siteId, instanceId, dependencies },
                environment = new { applicationId = ApplicationId, version, siteId, instanceId, environment = detected }
            };

            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/registrations/register");
            request.Headers.TryAddWithoutValidation("X-Aegis-Registration-Key", key);
            request.Content = new StringContent(JsonSerializer.Serialize(registration), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) { logger.LogWarning("Aegis.Configuration rejected Cafeteria Services registration with HTTP {StatusCode}; service remains operational and will retry.", (int)response.StatusCode); return false; }
            logger.LogInformation("Aegis.Cafeteria.Services published configuration, dependency and detected-environment contracts.");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { logger.LogWarning(ex, "Unable to publish Aegis.Cafeteria.Services operational contracts; service remains operational and will retry."); return false; }
    }

    private JsonArray BuildRequirements()
    {
        JsonArray r = [];
        Add(r, "service-url", "urls", "Service URL", true, "Address exposed by Cafeteria Services.", "http://0.0.0.0:5088");
        Add(r, "configuration-url", "Aegis:Configuration:Url", "System Configuration URL", false, "Aegis.Configuration endpoint.");
        Add(r, "diagnostics-url", "Aegis:Diagnostics:Url", "System Diagnostics URL", false, "Aegis.Diagnostics endpoint.");
        Add(r, "site-id", "Deployment:SiteId", "Site ID", false, "Deployment site identifier.");
        Add(r, "service-identity", "Service:Identity", "Service Identity", false, "Stable service instance identity.", Environment.MachineName);
        Add(r, "service-database", "ServicePersistence:ConnectionString", "Service Database", true, "SQLite connection string for Cafeteria Services state.", "Data Source=cafeteria.services.db");

        Add(r, "ffp-enabled", "FfpIntegration:Enabled", "FFP Integration Enabled", true, "FFP integration is required outside Development.", "true");
        Add(r, "ffp-server", "FfpIntegration:Server", "FFP SQL Server", true, "FFP SQL Server address. Windows Integrated Security is used.");
        Add(r, "ffp-database", "FfpIntegration:Database", "FFP Database", true, "FFP SQL database name.");
        Add(r, "ffp-encrypt", "FfpIntegration:Encrypt", "SQL Encryption", false, "Enable SQL transport encryption.", "true");
        Add(r, "ffp-trust-cert", "FfpIntegration:TrustServerCertificate", "Trust SQL Server Certificate", false, "Controls SQL certificate trust behavior.", "false");
        Add(r, "ffp-credit-code", "FfpIntegration:CreditLimitLunchItemCode", "Credit Limit Lunch Item Code", false, "Product code used to derive credit-limit rate.", "FFP00");
        Add(r, "ffp-subsidy-code", "FfpIntegration:SubsidyLunchItemCode", "Subsidy Lunch Item Code", false, "Product code used to derive subsidy rate.", "FFP00");
        Add(r, "ffp-monthly-scan", "FfpIntegration:MonthlyAllocationScanType", "Monthly Allocation Scan Type", false, "FFP transaction mapping.", "3");
        Add(r, "ffp-purchase-scan", "FfpIntegration:PurchaseScanType", "Purchase Scan Type", false, "FFP purchase transaction mapping.", "8");
        Add(r, "ffp-stipend-scan", "FfpIntegration:StipendScanType", "Stipend Scan Type", false, "FFP stipend transaction mapping.", "4");
        Add(r, "ffp-refund-scan", "FfpIntegration:RefundScanType", "Refund Scan Type", false, "FFP refund transaction mapping.", "9");
        Add(r, "ffp-adjustment-scan", "FfpIntegration:AdjustmentScanType", "Adjustment Scan Type", false, "FFP adjustment transaction mapping.", "9");

        Add(r, "security-enabled", "Security:Enabled", "Security Enabled", false, "Enables Cafeteria directory/security integration.", "true");
        Add(r, "security-domain", "Security:Domain", "AD Domain", true, "Active Directory domain used for authentication.");
        Add(r, "security-base", "Security:SearchBase", "AD Search Base", true, "LDAP search base.");
        Add(r, "security-port", "Security:Port", "Directory Port", false, "LDAP/LDAPS port.", "636");
        Add(r, "security-timeout", "Security:TimeoutSeconds", "Directory Timeout", false, "Directory request timeout in seconds.", "5");
        Add(r, "security-servers", "Security:Servers", "Directory Servers", true, "Domain controllers used for authentication.");
        Add(r, "security-admin-groups", "Security:AdministrativeGroups", "Administrative Groups", false, "Groups treated as Cafeteria administrators.");
        Add(r, "security-manager-window", "Security:ManagerAuthorizationMinutes", "Manager Authorization Minutes", false, "Duration of successful manager authorization.", "2");
        Add(r, "management-groups", "ManagementApi:AuthorizedGroups", "Management API Groups", false, "Groups permitted to perform management operations.");
        Add(r, "management-default-role", "ManagementApi:DefaultManagementRole", "Default Management Role", false, "Default management role.", "Supervisor");
        Add(r, "management-supervisors", "ManagementApi:RoleAssignments:Supervisors", "Supervisor Assignments", false, "Explicit supervisor role assignments.");
        Add(r, "management-managers", "ManagementApi:RoleAssignments:Managers", "Manager Assignments", false, "Explicit manager role assignments.");
        Add(r, "management-administrators", "ManagementApi:RoleAssignments:Administrators", "Administrator Assignments", false, "Explicit administrator role assignments.");

        Add(r, "month-opening", "MonthOpening:Enabled", "Month Opening Enabled", false, "Enable automatic month opening.", "true");
        Add(r, "month-opening-hour", "MonthOpening:RunHourLocal", "Month Opening Hour", false, "Local hour for month opening.", "1", "MonthOpening:Enabled=true");
        Add(r, "email-enabled", "ScheduledEmail:Enabled", "Scheduled Email Enabled", false, "Enable scheduled email reports.", "false");
        Add(r, "smtp-host", "ScheduledEmail:SmtpHost", "SMTP Host", false, "SMTP server used for scheduled emails.", null, "ScheduledEmail:Enabled=true");
        Add(r, "smtp-port", "ScheduledEmail:SmtpPort", "SMTP Port", false, "SMTP server port.", "25", "ScheduledEmail:Enabled=true");
        Add(r, "smtp-ssl", "ScheduledEmail:EnableSsl", "SMTP SSL", false, "Enable SMTP SSL/TLS.", "false", "ScheduledEmail:Enabled=true");
        Add(r, "smtp-from", "ScheduledEmail:FromAddress", "Email From Address", false, "Sender address for Cafeteria mail.", "cafeteria@localhost", "ScheduledEmail:Enabled=true");
        Add(r, "daily-recipients", "ScheduledEmail:DailySummaryRecipients", "Daily Summary Recipients", false, "Recipients for daily summary email.", null, "ScheduledEmail:Enabled=true");
        Add(r, "daily-hour", "ScheduledEmail:DailySummaryHourLocal", "Daily Summary Hour", false, "Daily summary local hour.", "17", "ScheduledEmail:Enabled=true");
        Add(r, "weekly-day", "ScheduledEmail:WeeklyEmployeeDay", "Weekly Employee Day", false, "Weekly employee summary day.", "Monday", "ScheduledEmail:Enabled=true");
        Add(r, "weekly-hour", "ScheduledEmail:WeeklyEmployeeHourLocal", "Weekly Employee Hour", false, "Weekly employee summary local hour.", "8", "ScheduledEmail:Enabled=true");
        Add(r, "employee-email-column", "ScheduledEmail:EmployeeEmailColumn", "Employee Email Column", false, "FFP employee field containing email address.", "Email", "ScheduledEmail:Enabled=true");
        Add(r, "payroll-enabled", "ScheduledEmail:PayrollNotificationEnabled", "Payroll Notification Enabled", false, "Enable payroll report email.", "true", "ScheduledEmail:Enabled=true");
        Add(r, "payroll-recipients", "ScheduledEmail:PayrollRecipients", "Payroll Recipients", false, "Recipients for payroll report email.", null, "ScheduledEmail:Enabled=true && ScheduledEmail:PayrollNotificationEnabled=true");
        Add(r, "payroll-hour", "ScheduledEmail:PayrollReportHourLocal", "Payroll Report Hour", false, "Payroll report local hour.", "6", "ScheduledEmail:Enabled=true && ScheduledEmail:PayrollNotificationEnabled=true");

        Add(r, "backup-enabled", "ServiceBackup:Enabled", "Service Backup Enabled", false, "Enable Services database backup.", "true");
        Add(r, "backup-directory", "ServiceBackup:Directory", "Backup Directory", false, "Backup destination path.", "backups", "ServiceBackup:Enabled=true");
        Add(r, "backup-hour", "ServiceBackup:RunHourLocal", "Backup Hour", false, "Local backup hour.", "2", "ServiceBackup:Enabled=true");
        Add(r, "backup-retention", "ServiceBackup:RetentionDays", "Backup Retention Days", false, "Number of days to retain backups.", "30", "ServiceBackup:Enabled=true");

        Add(r, "logging-app", "Logging:ApplicationName", "Logging Application Name", false, "Application identity sent to logging providers.", "Aegis.Cafeteria.Services");
        Add(r, "graylog-enabled", "Logging:GraylogEnabled", "Graylog Enabled", false, "Enable Graylog logging.", "true");
        Add(r, "graylog-host", "Logging:GraylogHost", "Graylog Host", false, "Graylog server address.", "10.10.0.3", "Logging:GraylogEnabled=true");
        Add(r, "graylog-port", "Logging:GraylogPort", "Graylog Port", false, "Graylog input port.", "12201", "Logging:GraylogEnabled=true");
        Add(r, "funding-credit", "FundingPolicy:DailyCreditLimitRate", "Bootstrap Daily Credit Limit Rate", false, "Optional initial funding policy value when the service database has no policy.", "0");
        Add(r, "funding-subsidy", "FundingPolicy:DailyCompanySubsidyRate", "Bootstrap Daily Company Subsidy Rate", false, "Optional initial subsidy policy value when the service database has no policy.", "0");
        Add(r, "development-data", "DevelopmentData:Enabled", "Development Data Enabled", false, "Enables simulated data only in Development.", "false");
        Add(r, "messaging-enabled", "Messaging:Enabled", "Messaging Enabled", false, "Enables optional Common.Messaging integration.", "false");
        Add(r, "messaging-endpoint", "Messaging:Endpoint", "Messaging Endpoint", false, "Optional Common.Messaging endpoint.", null, "Messaging:Enabled=true");
        Add(r, "health-enabled", "Health:Enabled", "Health Integration Enabled", false, "Enables optional health integration.", "false");

        AddSecret(r, "configuration-registration-key", "ConfigurationRegistrationKey", "Configuration Registration Credential", false, "Credential used to publish this contract.", $"configuration/registration/{ApplicationId}", RegistrationSecretDetected());
        AddSecret(r, "secret-zero", "Secret0", "OpenBao Secret Zero", false, "Bootstrap material used to authenticate to OpenBao/Common.Secrets. Value is never published.", "OpenBao/bootstrap", SecretZeroDetected());
        AddSecret(r, "management-api-key", "ManagementApi:ApiKey", "Management/POS API Key", false, "Shared service-to-service key where this legacy/optional authentication path is enabled.", "ManagementApi:ApiKey", SecretConfigurationDetected("ManagementApi:ApiKey"));
        AddSecret(r, "smtp-credential", "ScheduledEmail:SmtpCredential", "SMTP Credential", false, "SMTP credential when authenticated SMTP is configured.", "cafeteria/smtp", false, "ScheduledEmail:Enabled=true");
        return r;
    }

    private async Task<List<object>> BuildDependenciesAsync(string baseUrl, CancellationToken ct)
    {
        List<object> dependencies =
        [
            Dependency("service-db", "Cafeteria Services Database", true, "Startup", "Stores central Cafeteria Services state.", "Services cannot initialize its persistent operational state.", "Microsoft.Data.Sqlite / Entity Framework Core", "SQLite", configuration["ServicePersistence:ConnectionString"], null),
            Dependency("ffp-sql", "FFP Manager Database", !environment.IsDevelopment(), "Startup", "Provides FFP workforce, products, holidays, account and ledger integration.", "Production Cafeteria Services cannot start correctly without required FFP integration.", "Microsoft.Data.SqlClient with Windows Integrated Security", "Microsoft SQL Server", FfpEndpoint(), await ReachableTcpAsync(FfpHost(), FfpPort(), ct)),
            Dependency("active-directory", "Active Directory", true, "Feature", "Authenticates Cafeteria POS and management users and evaluates authorization groups.", "Unauthenticated health endpoints remain available, but user-facing authenticated operations are unavailable.", "LDAP/LDAPS via Common.Security", "Active Directory", DirectoryEndpoint(), await ReachableTcpAsync(FirstDirectoryServer(), configuration.GetValue("Security:Port", 636), ct)),
            Dependency("configuration", "Aegis.Configuration", false, "Feature", "Receives Cafeteria Services operational contracts.", "Services remains operational; centralized Configuration visibility is unavailable.", "HTTP/HTTPS JSON", "Aegis.Configuration", baseUrl, await ReachableHttpAsync(baseUrl, ct)),
            Dependency("diagnostics", "Aegis.Diagnostics", false, "Feature", "Central diagnostics visibility when configured.", "Services retains its local engineering diagnostics behavior.", "HTTP/HTTPS JSON", "Aegis.Diagnostics", configuration["Aegis:Diagnostics:Url"], await ReachableHttpAsync(configuration["Aegis:Diagnostics:Url"], ct))
        ];
        if (configuration.GetValue("Logging:GraylogEnabled", true))
            dependencies.Add(Dependency("graylog", "Graylog", false, "Feature", "Central structured logging.", "Services continues; centralized logs are unavailable.", "Configured Graylog logging provider", "Graylog", $"{configuration["Logging:GraylogHost"]}:{configuration.GetValue("Logging:GraylogPort", 12201)}", null));
        if (configuration.GetValue("ScheduledEmail:Enabled", false))
            dependencies.Add(Dependency("smtp", "SMTP Server", false, "Feature", "Sends scheduled Cafeteria reports and payroll notifications.", "Services continues; scheduled email delivery fails/retries according to worker behavior.", "SMTP", "SMTP server", $"{configuration["ScheduledEmail:SmtpHost"]}:{configuration.GetValue("ScheduledEmail:SmtpPort", 25)}", await ReachableTcpAsync(configuration["ScheduledEmail:SmtpHost"], configuration.GetValue("ScheduledEmail:SmtpPort", 25), ct)));
        if (configuration.GetValue("Messaging:Enabled", false))
            dependencies.Add(Dependency("messaging", "Common.Messaging Endpoint", false, "Feature", "Optional messaging integration.", "Core Cafeteria services continue; messaging delivery is unavailable.", "Configured Common.Messaging interface", "Common.Messaging", configuration["Messaging:Endpoint"], await ReachableHttpAsync(configuration["Messaging:Endpoint"], ct)));
        string? openBao = OpenBaoAddress();
        if (!string.IsNullOrWhiteSpace(openBao)) dependencies.Add(Dependency("openbao", "OpenBao", false, "Startup", "Supplies secret material through Common.Secrets.", "Secret-backed registration/authentication features cannot resolve their credentials.", "OpenBao HTTP API via Common.Secrets", "OpenBao", openBao, await ReachableHttpAsync(openBao, ct)));
        return dependencies;
    }

    private async Task<object> DetectEnvironmentAsync(string baseUrl, CancellationToken ct)
    {
        Process process = Process.GetCurrentProcess();
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        List<object> paths = [];
        List<object> disks = [];
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in RelevantPaths().Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string path;
            try { path = ResolvePath(raw); } catch { continue; }
            bool exists = Directory.Exists(path) || File.Exists(path);
            string directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
            bool? writable = Directory.Exists(directory) ? await IsWritableAsync(directory, ct) : null;
            paths.Add(new { path, exists, writable });
            try { string root = Path.GetPathRoot(path) ?? string.Empty; if (root.Length > 0 && roots.Add(root)) { DriveInfo d = new(root); if (d.IsReady) disks.Add(new { path = root, freeBytes = d.AvailableFreeSpace, totalBytes = d.TotalSize }); } } catch { }
        }

        List<object> endpoints = [];
        foreach ((string name, string? endpoint, bool? reachable) in new[]
        {
            ("Aegis.Configuration", (string?)baseUrl, await ReachableHttpAsync(baseUrl, ct)),
            ("Aegis.Diagnostics", configuration["Aegis:Diagnostics:Url"], await ReachableHttpAsync(configuration["Aegis:Diagnostics:Url"], ct)),
            ("OpenBao", OpenBaoAddress(), await ReachableHttpAsync(OpenBaoAddress(), ct))
        }) if (!string.IsNullOrWhiteSpace(endpoint)) endpoints.Add(new { name, endpoint, reachable });

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

    private IEnumerable<string> RelevantPaths()
    {
        yield return environment.ContentRootPath;
        yield return configuration["ServicePersistence:ConnectionString"] ?? "Data Source=cafeteria.services.db";
        if (configuration.GetValue("ServiceBackup:Enabled", true)) yield return configuration["ServiceBackup:Directory"] ?? "backups";
    }

    private void Add(JsonArray array, string id, string key, string name, bool required, string description, string? defaultValue = null, string? condition = null)
    {
        string? raw = configuration[key];
        string envName = key.Replace(":", "__", StringComparison.Ordinal);
        string? env = Environment.GetEnvironmentVariable(envName);
        string? value = First(env, raw, defaultValue);
        string source = !string.IsNullOrWhiteSpace(env) ? $"Environment Variable ({envName})" : !string.IsNullOrWhiteSpace(raw) ? "Configuration Provider" : defaultValue is not null ? "Code Default" : "Unresolved";
        array.Add(new JsonObject
        {
            ["id"] = id,
            ["displayName"] = name,
            ["kind"] = "Configuration",
            ["required"] = required,
            ["purpose"] = description,
            ["configurationKey"] = key,
            ["isConfigured"] = !string.IsNullOrWhiteSpace(value),
            ["safeDisplayValue"] = value,
            ["sensitive"] = false,
            ["conditionalOn"] = condition,
            ["allowedSources"] = new JsonArray("Environment Variable", $"appsettings.{environment.EnvironmentName}.json", "appsettings.json", "Code Default"),
            ["resolutionOrder"] = new JsonArray("Environment Variable", $"appsettings.{environment.EnvironmentName}.json", "appsettings.json", "Code Default"),
            ["effectiveSource"] = source,
            ["hasDefault"] = defaultValue is not null,
            ["defaultValue"] = defaultValue,
            ["redacted"] = false
        });
    }

    private static void AddSecret(JsonArray array, string id, string key, string name, bool required, string description, string secretPath, bool configured, string? condition = null)
    {
        array.Add(new JsonObject
        {
            ["id"] = id,
            ["displayName"] = name,
            ["kind"] = "Secret",
            ["required"] = required,
            ["purpose"] = description,
            ["configurationKey"] = key,
            ["isConfigured"] = configured,
            ["safeDisplayValue"] = null,
            ["sensitive"] = true,
            ["conditionalOn"] = condition,
            ["allowedSources"] = new JsonArray("OpenBao/Common.Secrets", "approved Environment Variable override", "Secret0/bootstrap"),
            ["resolutionOrder"] = new JsonArray("secure secret provider", "approved Environment Variable override", "no plaintext fallback"),
            ["effectiveSource"] = configured ? "Secure runtime source" : "Unresolved",
            ["hasDefault"] = false,
            ["defaultValue"] = null,
            ["secretPath"] = secretPath,
            ["redacted"] = true
        });
    }

    private static object Dependency(string id, string name, bool required, string criticality, string purpose, string degraded, string @interface, string? engine, string? endpoint, bool? reachable) => new
    {
        id,
        displayName = name,
        required,
        criticality,
        purpose,
        degradedBehavior = degraded,
        @interface,
        engine,
        endpoint,
        reachable,
        evidence = "Explicitly declared from Cafeteria Services code; engine is not inferred from connection strings."
    };

    private string? OpenBaoAddress() => First(configuration["Secrets:OpenBao:Address"], configuration["OpenBao:Address"], Environment.GetEnvironmentVariable("OPENBAO_ADDR"), Environment.GetEnvironmentVariable("VAULT_ADDR"));
    private static bool SecretZeroDetected() => new[] { "OPENBAO_SECRET_ID", "OPENBAO_TOKEN", "VAULT_TOKEN", "BWS_ACCESS_TOKEN" }.Any(n => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(n)));
    private static bool RegistrationSecretDetected() => SecretZeroDetected();
    private bool SecretConfigurationDetected(string key) => !string.IsNullOrWhiteSpace(configuration[key]) || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key.Replace(":", "__", StringComparison.Ordinal)));
    private string? FirstDirectoryServer() => configuration.GetSection("Security:Servers").GetChildren().Select(x => x.Value).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    private string? DirectoryEndpoint() { string? host = FirstDirectoryServer(); return string.IsNullOrWhiteSpace(host) ? null : $"{host}:{configuration.GetValue("Security:Port", 636)}"; }
    private string? FfpHost()
    {
        string? server = configuration["FfpIntegration:Server"];
        if (string.IsNullOrWhiteSpace(server)) return null;
        string value = server.Replace("tcp:", "", StringComparison.OrdinalIgnoreCase);
        int comma = value.LastIndexOf(',');
        return comma > 0 ? value[..comma] : value;
    }
    private int FfpPort()
    {
        string? server = configuration["FfpIntegration:Server"];
        if (string.IsNullOrWhiteSpace(server)) return 1433;
        int comma = server.LastIndexOf(',');
        return comma > 0 && int.TryParse(server[(comma + 1)..], out int port) ? port : 1433;
    }
    private string? FfpEndpoint() => FfpHost() is { } host ? $"{host}:{FfpPort()}" : null;
    private static string ResolvePath(string value)
    {
        if (value.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            Microsoft.Data.Sqlite.SqliteConnectionStringBuilder b = new(value);
            return Path.GetFullPath(b.DataSource);
        }
        return Path.GetFullPath(value);
    }
    private static async Task<bool?> ReachableHttpAsync(string? endpoint, CancellationToken ct)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) return null;
        try { using HttpClient c = new() { Timeout = TimeSpan.FromSeconds(3) }; using HttpResponseMessage _ = await c.SendAsync(new HttpRequestMessage(HttpMethod.Head, uri), ct); return true; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }
    private static async Task<bool?> ReachableTcpAsync(string? host, int port, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        try { using System.Net.Sockets.TcpClient c = new(); await c.ConnectAsync(host, port, ct); return true; }
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
}
