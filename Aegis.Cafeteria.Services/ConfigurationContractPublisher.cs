using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Common.Secrets;

namespace Aegis.Cafeteria.Services;

public sealed class ConfigurationContractPublisher(IConfiguration configuration, IWebHostEnvironment environment, ILogger<ConfigurationContractPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Services";
    private static readonly TimeSpan retryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string? baseUrl = First(configuration["Aegis:Configuration:Url"], configuration["AegisConfiguration:BaseUrl"]);
        if (string.IsNullOrWhiteSpace(baseUrl)) { logger.LogInformation("Aegis.Configuration publication is disabled because no Configuration URL is configured."); return; }
        string path = configuration["AegisConfiguration:ContractPath"] ?? Path.Combine(environment.ContentRootPath, "configuration", "Aegis.Cafeteria.Services.configuration-contract.json");
        while (!stoppingToken.IsCancellationRequested)
        {
            if (await TryPublishAsync(baseUrl, path, stoppingToken)) return;
            try { await Task.Delay(retryDelay, stoppingToken); } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<bool> TryPublishAsync(string baseUrl, string path, CancellationToken cancellationToken)
    {
        try
        {
            JsonObject contract = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?.AsObject() ?? new JsonObject()
                : new JsonObject();

            ISecretProvider secrets = CommonSecretProviderFactory.Create(configuration);
            string? key = await secrets.GetAsync($"configuration/registration/{ApplicationId}", cancellationToken);
            if (string.IsNullOrWhiteSpace(key)) { logger.LogWarning("Aegis.Configuration registration credential is unavailable; Cafeteria Services remains operational and will retry publication."); return false; }

            contract["applicationId"] = ApplicationId;
            contract["displayName"] = "Aegis Cafeteria Services";
            contract["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
            contract["siteId"] = NullIfBlank(First(configuration["Deployment:SiteId"], configuration["Site:Id"]));
            contract["instanceId"] = NullIfBlank(First(configuration["Service:Identity"], Environment.MachineName));
            contract["description"] = "Central synchronization, management, reporting, and FFP integration services for Aegis Cafeteria.";
            contract["requirements"] = BuildRequirements();

            using HttpClient client = new() { Timeout = TimeSpan.FromSeconds(10) };
            using HttpRequestMessage request = new(HttpMethod.Post, $"{baseUrl.TrimEnd('/')}/api/contracts/register");
            request.Headers.TryAddWithoutValidation("X-Configuration-Registration-Key", key);
            request.Headers.TryAddWithoutValidation("X-Aegis-Registration-Key", key);
            request.Content = new StringContent(contract.ToJsonString(new JsonSerializerOptions { WriteIndented = false }), Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) { logger.LogWarning("Aegis.Configuration rejected Cafeteria Services publication with HTTP {StatusCode}; service remains operational and will retry.", (int)response.StatusCode); return false; }
            logger.LogInformation("Aegis.Cafeteria.Services published its complete configuration contract to Aegis.Configuration.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { logger.LogWarning(exception, "Unable to publish Aegis.Cafeteria.Services configuration contract; service remains operational and will retry publication."); return false; }
    }

    private JsonArray BuildRequirements()
    {
        JsonArray requirements = [];
        Add(requirements, "service-url", "urls", "Service URL", true, false, "Address exposed by Cafeteria Services.", "http://0.0.0.0:5088");
        Add(requirements, "configuration-url", "Aegis:Configuration:Url", "System Configuration URL", false, false, "Aegis.Configuration endpoint used for contract publication.");
        Add(requirements, "diagnostics-url", "Aegis:Diagnostics:Url", "System Diagnostics URL", false, false, "Aegis.Diagnostics endpoint.");
        Add(requirements, "site-id", "Deployment:SiteId", "Site ID", false, false, "Deployment site identifier.");
        Add(requirements, "service-identity", "Service:Identity", "Service Identity", false, false, "Stable service instance identity.", Environment.MachineName);
        Add(requirements, "service-database", "ServicePersistence:ConnectionString", "Service Database", true, false, "SQLite connection string for Cafeteria Services state.", "Data Source=cafeteria.services.db");

        Add(requirements, "ffp-enabled", "FfpIntegration:Enabled", "FFP Integration Enabled", true, false, "FFP integration must be enabled in production.", "true");
        Add(requirements, "ffp-server", "FfpIntegration:Server", "FFP SQL Server", true, false, "FFP SQL Server address. Windows Integrated Security is used.");
        Add(requirements, "ffp-database", "FfpIntegration:Database", "FFP Database", true, false, "FFP SQL database name.");
        Add(requirements, "ffp-encrypt", "FfpIntegration:Encrypt", "SQL Encryption", false, false, "Enable SQL transport encryption.", "true");
        Add(requirements, "ffp-trust-cert", "FfpIntegration:TrustServerCertificate", "Trust SQL Server Certificate", false, false, "Controls SQL certificate trust behavior.", "false");
        Add(requirements, "ffp-credit-code", "FfpIntegration:CreditLimitLunchItemCode", "Credit Limit Lunch Item Code", false, false, "Product code used to derive credit-limit rate.", "FFP00");
        Add(requirements, "ffp-subsidy-code", "FfpIntegration:SubsidyLunchItemCode", "Subsidy Lunch Item Code", false, false, "Product code used to derive subsidy rate.", "FFP00");
        Add(requirements, "ffp-monthly-scan", "FfpIntegration:MonthlyAllocationScanType", "Monthly Allocation Scan Type", false, false, "FFP transaction mapping.", "3");
        Add(requirements, "ffp-purchase-scan", "FfpIntegration:PurchaseScanType", "Purchase Scan Type", false, false, "FFP purchase transaction mapping.", "8");
        Add(requirements, "ffp-stipend-scan", "FfpIntegration:StipendScanType", "Stipend Scan Type", false, false, "FFP stipend transaction mapping.", "4");
        Add(requirements, "ffp-refund-scan", "FfpIntegration:RefundScanType", "Refund Scan Type", false, false, "FFP refund transaction mapping.", "9");
        Add(requirements, "ffp-adjustment-scan", "FfpIntegration:AdjustmentScanType", "Adjustment Scan Type", false, false, "FFP adjustment transaction mapping.", "9");

        Add(requirements, "security-domain", "Security:Domain", "AD Domain", true, false, "Active Directory domain used for Cafeteria authentication.");
        Add(requirements, "security-base", "Security:SearchBase", "AD Search Base", true, false, "LDAP search base.");
        Add(requirements, "security-port", "Security:Port", "Directory Port", false, false, "LDAP/LDAPS port.", "636");
        Add(requirements, "security-servers", "Security:Servers", "Directory Servers", true, false, "Domain controllers used for authentication.");
        Add(requirements, "security-admin-groups", "Security:AdministrativeGroups", "Administrative Groups", false, false, "Groups treated as Cafeteria administrators.");
        Add(requirements, "management-groups", "ManagementApi:AuthorizedGroups", "Management API Groups", false, false, "Groups allowed to perform management operations.");

        Add(requirements, "month-opening", "MonthOpening:Enabled", "Month Opening Enabled", false, false, "Enable automatic month opening.", "true");
        Add(requirements, "month-opening-hour", "MonthOpening:RunHourLocal", "Month Opening Hour", false, false, "Local hour for month-opening worker.", "1", "MonthOpening:Enabled=true");

        Add(requirements, "email-enabled", "ScheduledEmail:Enabled", "Scheduled Email Enabled", false, false, "Enable scheduled email reports.", "false");
        Add(requirements, "smtp-host", "ScheduledEmail:SmtpHost", "SMTP Host", false, false, "SMTP server used for scheduled emails.", null, "ScheduledEmail:Enabled=true");
        Add(requirements, "smtp-port", "ScheduledEmail:SmtpPort", "SMTP Port", false, false, "SMTP server port.", "25", "ScheduledEmail:Enabled=true");
        Add(requirements, "smtp-ssl", "ScheduledEmail:EnableSsl", "SMTP SSL", false, false, "Enable SMTP SSL/TLS.", "false", "ScheduledEmail:Enabled=true");
        Add(requirements, "smtp-from", "ScheduledEmail:FromAddress", "Email From Address", false, false, "Sender address for Cafeteria mail.", "cafeteria@localhost", "ScheduledEmail:Enabled=true");
        Add(requirements, "daily-recipients", "ScheduledEmail:DailySummaryRecipients", "Daily Summary Recipients", false, false, "Recipients for daily summary email.", null, "ScheduledEmail:Enabled=true");
        Add(requirements, "payroll-recipients", "ScheduledEmail:PayrollRecipients", "Payroll Recipients", false, false, "Recipients for payroll report email.", null, "ScheduledEmail:Enabled=true && ScheduledEmail:PayrollNotificationEnabled=true");

        Add(requirements, "logging-app", "Logging:ApplicationName", "Logging Application Name", false, false, "Application identity sent to logging providers.", "Aegis.Cafeteria.Services");
        Add(requirements, "graylog-enabled", "Logging:GraylogEnabled", "Graylog Enabled", false, false, "Enable Graylog logging.", "true");
        Add(requirements, "graylog-host", "Logging:GraylogHost", "Graylog Host", false, false, "Graylog server address.", "10.10.0.3", "Logging:GraylogEnabled=true");
        Add(requirements, "graylog-port", "Logging:GraylogPort", "Graylog Port", false, false, "Graylog input port.", "12201", "Logging:GraylogEnabled=true");

        AddSecret(requirements, "configuration-registration-key", "ConfigurationRegistrationKey", "Configuration Registration Credential", false, "Credential used to publish this contract to Aegis.Configuration.", "configuration/registration/Aegis.Cafeteria.Services");
        AddSecret(requirements, "openbao-secret-zero", "Secret0", "OpenBao Secret Zero", true, "Bootstrap material used to authenticate to OpenBao. The value is never published.", "bootstrap/OpenBao");
        AddSecret(requirements, "smtp-credential", "ScheduledEmail:SmtpCredential", "SMTP Credential", false, "SMTP credential when authenticated SMTP is used.", "cafeteria/smtp", "ScheduledEmail:Enabled=true");
        return requirements;
    }

    private void Add(JsonArray array, string id, string key, string name, bool required, bool sensitive, string description, string? defaultValue = null, string? condition = null)
    {
        string? raw = configuration[key];
        string envName = key.Replace(":", "__", StringComparison.Ordinal);
        string? env = Environment.GetEnvironmentVariable(envName);
        bool configured = !string.IsNullOrWhiteSpace(env) || !string.IsNullOrWhiteSpace(raw) || defaultValue is not null;
        string source = !string.IsNullOrWhiteSpace(env) ? $"Environment Variable ({envName})" : !string.IsNullOrWhiteSpace(raw) ? "appsettings/environment configuration" : defaultValue is not null ? "Code Default" : "Unresolved";
        array.Add(new JsonObject
        {
            ["id"] = id,
            ["key"] = key,
            ["displayName"] = name,
            ["description"] = description,
            ["required"] = required,
            ["conditionalOn"] = condition,
            ["sensitive"] = sensitive,
            ["allowedSources"] = new JsonArray("Environment Variable", "appsettings.{Environment}.json", "appsettings.json", "Code Default"),
            ["resolutionOrder"] = new JsonArray("Environment Variable", "appsettings.{Environment}.json", "appsettings.json", "Code Default"),
            ["effectiveSource"] = source,
            ["isConfigured"] = configured,
            ["hasDefault"] = defaultValue is not null,
            ["defaultValue"] = defaultValue,
            ["safeDisplayValue"] = sensitive ? null : (!string.IsNullOrWhiteSpace(env) ? env : !string.IsNullOrWhiteSpace(raw) ? raw : defaultValue)
        });
    }

    private void AddSecret(JsonArray array, string id, string logicalName, string name, bool required, string description, string secretPath, string? condition = null)
    {
        array.Add(new JsonObject
        {
            ["id"] = id,
            ["key"] = logicalName,
            ["displayName"] = name,
            ["description"] = description,
            ["required"] = required,
            ["conditionalOn"] = condition,
            ["sensitive"] = true,
            ["allowedSources"] = new JsonArray("OpenBao", "Secret0 bootstrap", "approved Environment Variable override"),
            ["resolutionOrder"] = new JsonArray("OpenBao", "approved Environment Variable override", "no plaintext fallback"),
            ["effectiveSource"] = "OpenBao/Common.Secrets",
            ["secretPath"] = secretPath,
            ["isConfigured"] = true,
            ["safeDisplayValue"] = null,
            ["redacted"] = true
        });
    }

    private static string? First(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
