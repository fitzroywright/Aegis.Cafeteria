using Aegis.Cafeteria.Services.Engineering.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json.Nodes;
using Common.Registration;
using Common.Secrets;

namespace Aegis.Cafeteria.Services;

public sealed class ConfigurationContractPublisher(IConfiguration configuration, IWebHostEnvironment environment, ILogger<ConfigurationContractPublisher> logger) : BackgroundService
{
    private const string ApplicationId = "Aegis.Cafeteria.Services";
    private static readonly TimeSpan publishInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string configurationUrl = AegisControlPlaneEndpoints.ResolvePublic(
            configuration,
            AegisControlPlaneService.Configuration,
            logger,
            configurationKey: "Aegis:Configuration:Url");

        string path = configuration["AegisConfiguration:ContractPath"]
            ?? Path.Combine(environment.ContentRootPath, "configuration", "Aegis.Cafeteria.Services.configuration-contract.json");

        while (!stoppingToken.IsCancellationRequested)
        {
            await TryPublishAsync(configurationUrl, path, stoppingToken);
            try { await Task.Delay(publishInterval, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }

    private async Task<bool> TryPublishAsync(string configurationUrl, string path, CancellationToken cancellationToken)
    {
        try
        {
            JsonObject contract = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?.AsObject() ?? new JsonObject()
                : new JsonObject();

            ISecretProvider secrets = CommonSecretProviderFactory.Create(configuration);
            contract["applicationId"] = ApplicationId;
            contract["displayName"] = "Aegis Cafeteria Services";
            contract["version"] = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
            contract["siteId"] = NullIfBlank(First(configuration["Deployment:SiteId"], configuration["Site:Id"]));
            contract["instanceId"] = NullIfBlank(First(configuration["Service:Identity"], Environment.MachineName));
            contract["description"] = "Central synchronization, management, reporting, and FFP integration services for Aegis Cafeteria.";
            contract["presentation"] = new JsonObject
            {
                ["iconUrl"] = configuration["Aegis:Presentation:IconUrl"],
                ["shortName"] = "Cafeteria",
                ["accent"] = configuration["Aegis:Presentation:Accent"] ?? "gold"
            };
            contract["runtimeEnvironment"] = DetectedRuntimeEnvironment.Capture(environment.EnvironmentName, environment.ContentRootPath);
            contract["diagnostics"] = CafeteriaDiagnosticLevelContract.BuildCapability(
                configuration,
                NullIfBlank(configuration["Aegis:PublicUrl"]));
            contract["requirements"] = await BuildRequirementsAsync(secrets, cancellationToken);

            string instanceId =
                contract["instanceId"]?.GetValue<string>()
                ?? Environment.MachineName;

            string identityFile =
                configuration["Aegis:Registration:IdentityFile"]
                ?? (OperatingSystem.IsWindows()
                    ? Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "Aegis",
                        "Cafeteria.Services",
                        "registration-identity.json")
                    : "/var/lib/aegis/cafeteria-services/registration-identity.json");

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

            RegistrationLifecycleStatus status =
                await registration.StepAsync(
                    new JsonObject
                    {
                        ["displayName"] = "Aegis Cafeteria Services",
                        ["module"] = "Aegis.Cafeteria.Services",
                        ["presentation"] = new JsonObject
                        {
                            ["iconUrl"] = configuration["Aegis:Presentation:IconUrl"],
                            ["shortName"] = "Cafeteria",
                            ["accent"] = configuration["Aegis:Presentation:Accent"] ?? "gold"
                        },
                        ["siteId"] = contract["siteId"]?.DeepClone(),
                        ["publicUrl"] = configuration["Aegis:PublicUrl"],
                        ["hostname"] = Environment.MachineName,
                        ["runtimeEnvironment"] = environment.EnvironmentName
                    },
                    cancellationToken);

            if (!status.IsRegistered)
            {
                logger.LogInformation(
                    "Aegis.Cafeteria.Services registration state is {RegistrationState}: {RegistrationError}; service remains operational.",
                    status.State,
                    status.Error ?? "No additional detail.");
                return false;
            }

            HttpStatusCode contractStatus =
                await registration.PublishConfigurationContractAsync(contract, cancellationToken);

            if ((int)contractStatus is >= 200 and < 300)
            {
                logger.LogInformation(
                    "Aegis.Cafeteria.Services registration is valid and its configuration contract was published.");
                return true;
            }

            logger.LogWarning(
                "Aegis.Cafeteria.Services is registered, but Configuration contract publication returned HTTP {StatusCode}.",
                (int)contractStatus);
            return false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            using IDisposable? scope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["Activity"] = "Registration",
                ["Application"] = ApplicationId,
                ["RegistrationAttemptId"] = Guid.NewGuid().ToString("N"),
                ["Stage"] = "UnhandledFailure"
            });
            logger.LogWarning(exception, "Unable to register Aegis.Cafeteria.Services; service remains operational and will retry.");
            return false;
        }
    }

    private async Task<JsonArray> BuildRequirementsAsync(ISecretProvider secrets, CancellationToken cancellationToken)
    {
        JsonArray requirements = [];
        Add(requirements, "public-url", "Aegis:PublicUrl", "Public Service URL", true, false, "Canonical URL used by Aegis.Diagnostics and other Aegis services to reach Cafeteria Services.");
        Add(requirements, "service-url", "urls", "Service Listener", true, false, "Address on which Cafeteria Services listens locally.");
        Add(requirements, "configuration-url", "Aegis:Configuration:Url", "System Configuration URL", false, false, "Aegis.Configuration endpoint used for contract publication.");
        Add(requirements, "diagnostics-url", "Aegis:Diagnostics:Url", "System Diagnostics URL", false, false, "Aegis.Diagnostics endpoint.");
        Add(requirements, "diagnostic-level-secret-name", "Aegis:Diagnostics:RequestCredentialSecretName", "DiagnosticLevel Request Credential", true, true, "Logical secret name used for signed remote DiagnosticLevel execution.");
        Add(requirements, "site-id", "Deployment:SiteId", "Site ID", false, false, "Deployment site identifier.");
        Add(requirements, "service-identity", "Service:Identity", "Service Identity", false, false, "Stable service instance identity.", Environment.MachineName);
        Add(requirements, "service-database", "ServicePersistence:ConnectionString", "Service Database", true, false, "SQLite connection string for Cafeteria Services state.");

        Add(requirements, "ffp-enabled", "FfpIntegration:Enabled", "FFP Integration Enabled", true, false, "FFP integration must be enabled in production.");
        Add(requirements, "ffp-server", "FfpIntegration:Server", "FFP SQL Server", true, false, "FFP SQL Server address. Windows Integrated Security is used.");
        Add(requirements, "ffp-database", "FfpIntegration:Database", "FFP Database", true, false, "FFP SQL database name.");
        Add(requirements, "ffp-encrypt", "FfpIntegration:Encrypt", "SQL Encryption", false, false, "Enable SQL transport encryption.");
        Add(requirements, "ffp-trust-cert", "FfpIntegration:TrustServerCertificate", "Trust SQL Server Certificate", false, false, "Controls SQL certificate trust behavior.");
        Add(requirements, "ffp-credit-code", "FfpIntegration:CreditLimitLunchItemCode", "Credit Limit Lunch Item Code", false, false, "Product code used to derive credit-limit rate.");
        Add(requirements, "ffp-subsidy-code", "FfpIntegration:SubsidyLunchItemCode", "Subsidy Lunch Item Code", false, false, "Product code used to derive subsidy rate.");
        Add(requirements, "ffp-monthly-scan", "FfpIntegration:MonthlyAllocationScanType", "Monthly Allocation Scan Type", false, false, "FFP transaction mapping.");
        Add(requirements, "ffp-purchase-scan", "FfpIntegration:PurchaseScanType", "Purchase Scan Type", false, false, "FFP purchase transaction mapping.");
        Add(requirements, "ffp-stipend-scan", "FfpIntegration:StipendScanType", "Stipend Scan Type", false, false, "FFP stipend transaction mapping.");
        Add(requirements, "ffp-refund-scan", "FfpIntegration:RefundScanType", "Refund Scan Type", false, false, "FFP refund transaction mapping.");
        Add(requirements, "ffp-adjustment-scan", "FfpIntegration:AdjustmentScanType", "Adjustment Scan Type", false, false, "FFP adjustment transaction mapping.");

        Add(requirements, "security-domain", "Security:Domain", "AD Domain", true, false, "Active Directory domain used for Cafeteria authentication.");
        Add(requirements, "security-base", "Security:SearchBase", "AD Search Base", true, false, "LDAP search base.");
        Add(requirements, "security-port", "Security:Port", "Directory Port", false, false, "LDAP/LDAPS port.");
        Add(requirements, "security-servers", "Security:Servers", "Directory Servers", true, false, "Domain controllers used for authentication.");
        Add(requirements, "security-admin-groups", "Security:AdministrativeGroups", "Administrative Groups", false, false, "Groups treated as Cafeteria administrators.");
        Add(requirements, "management-groups", "ManagementApi:AuthorizedGroups", "Management API Groups", false, false, "Groups allowed to perform management operations.");

        Add(requirements, "external-publication-batch", "ExternalPublication:BatchSize", "External Publication Batch Size", false, false, "Maximum service-owned external ledger operations processed in one publication cycle.");
        Add(requirements, "external-publication-retry", "ExternalPublication:RetryIntervalSeconds", "External Publication Retry Interval", false, false, "Seconds between service-owned external ledger publication cycles.");
        Add(requirements, "external-publication-poison", "ExternalPublication:PoisonMessageAttemptLimit", "External Publication Poison Attempt Limit", false, false, "Maximum failed publication attempts before an external ledger operation is held for operator attention.");

        Add(requirements, "month-opening", "MonthOpening:Enabled", "Month Opening Enabled", false, false, "Enable automatic month opening.");
        Add(requirements, "month-opening-hour", "MonthOpening:RunHourLocal", "Month Opening Hour", false, false, "Local hour for month-opening worker.", null, "MonthOpening:Enabled=true");

        Add(requirements, "email-enabled", "ScheduledEmail:Enabled", "Scheduled Email Enabled", false, false, "Enable scheduled email reports.");
        Add(requirements, "smtp-host", "ScheduledEmail:SmtpHost", "SMTP Host", false, false, "SMTP server used for scheduled emails.", null, "ScheduledEmail:Enabled=true");
        Add(requirements, "smtp-port", "ScheduledEmail:SmtpPort", "SMTP Port", false, false, "SMTP server port.", null, "ScheduledEmail:Enabled=true");
        Add(requirements, "smtp-ssl", "ScheduledEmail:EnableSsl", "SMTP SSL", false, false, "Enable SMTP SSL/TLS.", null, "ScheduledEmail:Enabled=true");
        Add(requirements, "smtp-from", "ScheduledEmail:FromAddress", "Email From Address", false, false, "Sender address for Cafeteria mail.", null, "ScheduledEmail:Enabled=true");
        Add(requirements, "daily-recipients", "ScheduledEmail:DailySummaryRecipients", "Daily Summary Recipients", false, false, "Recipients for daily summary email.", null, "ScheduledEmail:Enabled=true");
        Add(requirements, "payroll-recipients", "ScheduledEmail:PayrollRecipients", "Payroll Recipients", false, false, "Recipients for payroll report email.", null, "ScheduledEmail:Enabled=true && ScheduledEmail:PayrollNotificationEnabled=true");

        Add(requirements, "logging-app", "Logging:ApplicationName", "Logging Application Name", false, false, "Application identity sent to logging providers.");
        Add(requirements, "graylog-enabled", "Logging:GraylogEnabled", "Graylog Enabled", false, false, "Enable Graylog logging.");
        Add(requirements, "graylog-host", "Logging:GraylogHost", "Graylog Host", false, false, "Graylog server address.", null, "Logging:GraylogEnabled=true");
        Add(requirements, "graylog-port", "Logging:GraylogPort", "Graylog Port", false, false, "Graylog input port.", null, "Logging:GraylogEnabled=true");

        SecretManagerConfigurationContractRequirements.Add(requirements, configuration);

        await AddSecretAsync(requirements, secrets, "openbao-secret-zero", "Secret0", "OpenBao Secret Zero", true, "Bootstrap material used to authenticate to OpenBao. The value is never published.", "bootstrap/OpenBao", directlyProbeable: false, cancellationToken: cancellationToken);
        await AddSecretAsync(requirements, secrets, "smtp-credential", "ScheduledEmail:SmtpCredential", "SMTP Credential", false, "SMTP credential when authenticated SMTP is used.", "cafeteria/smtp", "ScheduledEmail:Enabled=true", cancellationToken: cancellationToken);
        return requirements;
    }

    private void Add(JsonArray array, string id, string key, string name, bool required, bool sensitive, string description, string? defaultValue = null, string? condition = null)
    {
        defaultValue ??= CafeteriaConfigurationCodeDefaults.Get(key);
        IConfigurationSection section = configuration.GetSection(key);
        string? scalarValue = NullIfBlank(section.Value);
        string envName = key.Replace(":", "__", StringComparison.Ordinal);
        string? exactEnvironmentValue = NullIfBlank(Environment.GetEnvironmentVariable(envName));
        bool hasChildren = HasConfiguredChildren(section);
        bool explicitlyConfigured = exactEnvironmentValue is not null || scalarValue is not null || hasChildren;
        bool hasDefault = defaultValue is not null;
        bool effectiveValueAvailable = explicitlyConfigured || hasDefault;
        string source = exactEnvironmentValue is not null
            ? $"Environment Variable ({envName})"
            : explicitlyConfigured
                ? "Resolved IConfiguration"
                : hasDefault
                    ? "Runtime/Code Default"
                    : "Unresolved";
        string state = explicitlyConfigured ? "Configured" : hasDefault ? "Default" : "Unresolved";

        array.Add(new JsonObject
        {
            ["id"] = id,
            ["displayName"] = name,
            ["kind"] = "Configuration",
            ["required"] = required,
            ["purpose"] = description,
            ["configurationKey"] = key,
            ["key"] = key,
            ["description"] = description,
            ["conditionalOn"] = condition,
            ["sensitive"] = sensitive,
            ["allowedSources"] = new JsonArray("Environment Variable", "appsettings.{Environment}.json", "appsettings.json", "Runtime/Code Default"),
            ["resolutionOrder"] = new JsonArray("Environment Variable", "appsettings.{Environment}.json", "appsettings.json", "Runtime/Code Default"),
            ["effectiveSource"] = source,
            ["configurationState"] = state,
            ["isConfigured"] = explicitlyConfigured,
            ["effectiveValueAvailable"] = effectiveValueAvailable,
            ["displayValue"] = id == "public-url" && !sensitive ? scalarValue ?? defaultValue : null,
            ["hasDefault"] = hasDefault
        });
    }

    private async Task AddSecretAsync(JsonArray array, ISecretProvider secrets, string id, string logicalName, string name, bool required, string description, string secretPath, string? condition = null, bool directlyProbeable = true, CancellationToken cancellationToken = default)
    {
        bool? configured = null;
        string verificationStatus;

        if (!directlyProbeable)
        {
            verificationStatus = "NotDirectlyVerifiable";
        }
        else
        {
            try
            {
                configured = !string.IsNullOrWhiteSpace(await secrets.GetAsync(secretPath, cancellationToken));
                verificationStatus = configured.Value ? "Resolved" : "Missing";
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Unable to verify secret requirement {SecretRequirement}; publication will report its state as unknown.", id);
                verificationStatus = "VerificationFailed";
            }
        }

        array.Add(new JsonObject
        {
            ["id"] = id,
            ["displayName"] = name,
            ["kind"] = "Secret",
            ["required"] = required,
            ["purpose"] = description,
            ["secretName"] = secretPath,
            ["key"] = logicalName,
            ["description"] = description,
            ["conditionalOn"] = condition,
            ["sensitive"] = true,
            ["allowedSources"] = new JsonArray("OpenBao/Common.Secrets", "Secret0 bootstrap", "approved Environment Variable override"),
            ["resolutionOrder"] = new JsonArray("OpenBao/Common.Secrets", "approved Environment Variable override", "no plaintext fallback"),
            ["effectiveSource"] = directlyProbeable ? "Common.Secrets" : "Common.Secrets bootstrap",
            ["secretPath"] = secretPath,
            ["configurationState"] = configured == true ? "Configured" : configured == false ? "Unresolved" : "Unknown",
            ["isConfigured"] = configured,
            ["effectiveValueAvailable"] = configured,
            ["verificationStatus"] = verificationStatus,
            ["redacted"] = true
        });
    }

    private static bool HasConfiguredChildren(IConfigurationSection section)
        => section.GetChildren().Any(child => NullIfBlank(child.Value) is not null || HasConfiguredChildren(child));

    private static string? First(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
