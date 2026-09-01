namespace Aegis.Cafeteria.Infrastructure.Configuration;

using Aegis.Cafeteria.Application.Abstractions;
using Microsoft.Extensions.Configuration;

public static class CafeteriaRuntimeOptionsResolver
{
    public static BitwardenSecretsManagerOptions ReadSecretsManagerOptions(IConfiguration configuration)
    {
        IConfigurationSection section = configuration.GetSection("SecretsManager");
        Dictionary<string, string> ids = section.GetSection("SecretIds").GetChildren()
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .ToDictionary(x => x.Key, x => x.Value!, StringComparer.OrdinalIgnoreCase);
        return new BitwardenSecretsManagerOptions
        {
            CliPath = section["CliPath"] ?? "bws",
            AccessTokenEnvironmentVariable = section["AccessTokenEnvironmentVariable"] ?? "BWS_ACCESS_TOKEN",
            ServerUrl = NullIfWhiteSpace(section["ServerUrl"]),
            CommandTimeout = TimeSpan.FromSeconds(section.GetValue("CommandTimeoutSeconds", 15)),
            SecretIds = ids
        };
    }

    public static async Task<CafeteriaRuntimeOptions> ResolveAsync
    (
          IConfiguration configuration
        , IApplicationSecretProvider secrets
        , CancellationToken cancellationToken = default
        , bool allowDirectFfpDatabase = true
    )
    {
        IConfigurationSection p = configuration.GetSection("Persistence");
        IConfigurationSection log = configuration.GetSection("Logging");
        IConfigurationSection ffp = configuration.GetSection("FfpIntegration");
        IConfigurationSection health = configuration.GetSection("Health");
        IConfigurationSection messaging = configuration.GetSection("Messaging");
        IConfigurationSection svc = configuration.GetSection("CafeteriaServices");
        BitwardenSecretsManagerOptions sm = ReadSecretsManagerOptions(configuration);
        bool servicesEnabled = svc.GetValue("Enabled", false);
        bool developmentDataEnabled = configuration.GetSection("DevelopmentData").GetValue("Enabled", false);
        bool ffpEnabled = ffp.GetValue("Enabled", true);
        string? configuredFfpConnectionString = ffp["ConnectionString"];
        string ffpConnectionString = string.Empty;

        if (allowDirectFfpDatabase && ffpEnabled && !servicesEnabled)
        {
            ffpConnectionString = !string.IsNullOrWhiteSpace(configuredFfpConnectionString)
                ? configuredFfpConnectionString.Trim()
                : await secrets.GetSecretAsync(ApplicationSecretNames.FfpDatabaseConnectionString, cancellationToken)
                    ?? string.Empty;

            if (!developmentDataEnabled && string.IsNullOrWhiteSpace(ffpConnectionString))
            {
                throw new InvalidOperationException("FFP integration is enabled but no FfpIntegration:ConnectionString or FfpDatabaseConnectionString secret is configured.");
            }
        }

        string? messagingToken = messaging.GetValue("Enabled", false)
            ? await secrets.GetSecretAsync(ApplicationSecretNames.MessagingBearerToken, cancellationToken)
            : null;

        CafeteriaPersistenceOptions persistence = new()
        {
            LocalSqliteConnectionString = p["LocalSqliteConnectionString"] ?? "Data Source=cafeteria.local.db;Cache=Shared",
            TerminalId = p["TerminalId"] ?? Environment.MachineName,
            FfpPublicationBatchSize = p.GetValue("FfpPublicationBatchSize", 100),
            FfpPoisonMessageAttemptLimit = p.GetValue("FfpPoisonMessageAttemptLimit", 25)
        };
        CafeteriaLoggingOptions logging = new()
        {
            ApplicationName = log["ApplicationName"] ?? "Aegis.Cafeteria.POS",
            GraylogEnabled = log.GetValue("GraylogEnabled", true),
            GraylogHost = log["GraylogHost"] ?? "10.10.0.3",
            GraylogPort = log.GetValue("GraylogPort", 12201),
            LocalLogPath = log["LocalLogPath"] ?? "",
            RetainedFileCountLimit = log.GetValue("RetainedFileCountLimit", 31)
        };
        FfpIntegrationOptions ffpOptions = new()
        {
            Enabled = ffpEnabled,
            ConnectionString = ffpConnectionString,
            AllowWrites = ffp.GetValue("AllowWrites", true),
            RefreshIntervalMinutes = Math.Max(1, ffp.GetValue("RefreshIntervalMinutes", 10)),
            CreditLimitLunchItemCode = ffp["CreditLimitLunchItemCode"]?.Trim() ?? "FFP00",
            SubsidyLunchItemCode = ffp["SubsidyLunchItemCode"]?.Trim() ?? "FFP00",
            MonthlyAllocationScanType = ffp.GetValue("MonthlyAllocationScanType", 3),
            PurchaseScanType = ffp.GetValue("PurchaseScanType", 8),
            StipendScanType = ffp.GetValue("StipendScanType", 4),
            RefundScanType = ffp.GetValue("RefundScanType", 9),
            AdjustmentScanType = ffp.GetValue("AdjustmentScanType", 9)
        };
        CafeteriaHealthOptions healthOptions = new()
        {
            Enabled = health.GetValue("Enabled", true),
            IntervalSeconds = Math.Max(15, health.GetValue("IntervalSeconds", 60)),
            HeartbeatPath = health["HeartbeatPath"] ?? @"C:\ProgramData\Aegis.Cafeteria\Health\heartbeat.json",
            GraylogHeartbeatEveryMinutes = Math.Max(1, health.GetValue("GraylogHeartbeatEveryMinutes", 5)),
            EmployeeCacheWarningMinutes = Math.Max(1, health.GetValue("EmployeeCacheWarningMinutes", 120)),
            EmployeeCacheCriticalMinutes = Math.Max(2, health.GetValue("EmployeeCacheCriticalMinutes", 480))
        };
        CafeteriaMessagingOptions messagingOptions = new()
        {
            Enabled = messaging.GetValue("Enabled", false),
            Endpoint = messaging["Endpoint"] ?? string.Empty,
            BearerToken = messagingToken,
            WeeklySummaryDay = Enum.TryParse<DayOfWeek>(messaging["WeeklySummaryDay"], true, out DayOfWeek day) ? day : DayOfWeek.Monday,
            WeeklySummaryHourLocal = Math.Clamp(messaging.GetValue("WeeklySummaryHourLocal", 8), 0, 23)
        };
        string? configuredServiceApiKey = svc["ApiKey"];
        string? serviceApiKey = servicesEnabled
            ? !string.IsNullOrWhiteSpace(configuredServiceApiKey)
                ? configuredServiceApiKey.Trim()
                : await secrets.GetSecretAsync(ApplicationSecretNames.CafeteriaServicesApiKey, cancellationToken)
            : null;
        CafeteriaServicesOptions servicesOptions = new()
        {
            Enabled = servicesEnabled,
            BaseUrl = svc["BaseUrl"]?.Trim() ?? "http://localhost:5088",
            PollSeconds = Math.Max(5, svc.GetValue("PollSeconds", 15)),
            ApiKey = string.IsNullOrWhiteSpace(serviceApiKey) ? null : serviceApiKey.Trim(),
            UseServiceMonthlyAllocations = svc.GetValue("UseServiceMonthlyAllocations", false),
            RealtimeEnabled = svc.GetValue("RealtimeEnabled", true),
            HubPath = svc["HubPath"]?.Trim() ?? "/hubs/cafeteria",
            RealtimeReconnectSeconds = Math.Max(2, svc.GetValue("RealtimeReconnectSeconds", 5))
        };
        return new CafeteriaRuntimeOptions(persistence, logging, sm, ffpOptions, healthOptions, messagingOptions, servicesOptions);
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
