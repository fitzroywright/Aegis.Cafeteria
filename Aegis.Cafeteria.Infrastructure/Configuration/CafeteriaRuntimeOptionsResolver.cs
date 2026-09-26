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

    public static async Task<CafeteriaRuntimeOptions> ResolvePosAsync(
        IConfiguration configuration,
        string registrationApplicationId = "Aegis.Cafeteria.POS",
        string registrationDisplayName = "Aegis Cafeteria POS",
        CancellationToken cancellationToken = default)
    {
        CafeteriaRuntimeOptions runtime = await ResolveCoreAsync(
            configuration,
            secrets: null,
            allowDirectFfpDatabase: false,
            allowRemoteSecrets: false,
            cancellationToken);

        string? siteId = NullIfWhiteSpace(configuration["Deployment:SiteId"]);
        await CafeteriaConfigurationRegistration.TryPublishPosAsync(
            configuration,
            runtime,
            siteId,
            registrationApplicationId,
            registrationDisplayName,
            cancellationToken);
        return runtime;
    }

    public static Task<CafeteriaRuntimeOptions> ResolvePosAsync(
        IConfiguration configuration,
        IApplicationSecretProvider _,
        string registrationApplicationId = "Aegis.Cafeteria.POS",
        string registrationDisplayName = "Aegis Cafeteria POS",
        CancellationToken cancellationToken = default)
    {
        return ResolvePosAsync(
            configuration,
            registrationApplicationId,
            registrationDisplayName,
            cancellationToken);
    }

    public static Task<CafeteriaRuntimeOptions> ResolveAsync(
        IConfiguration configuration,
        IApplicationSecretProvider secrets,
        CancellationToken cancellationToken = default)
    {
        return ResolveCoreAsync(
            configuration,
            secrets,
            allowDirectFfpDatabase: true,
            allowRemoteSecrets: true,
            cancellationToken);
    }

    private static async Task<CafeteriaRuntimeOptions> ResolveCoreAsync(
        IConfiguration configuration,
        IApplicationSecretProvider? secrets,
        bool allowDirectFfpDatabase,
        bool allowRemoteSecrets,
        CancellationToken cancellationToken)
    {
        IConfigurationSection p = configuration.GetSection("Persistence");
        IConfigurationSection log = configuration.GetSection("Logging");
        IConfigurationSection health = configuration.GetSection("Health");
        IConfigurationSection messaging = configuration.GetSection("Messaging");
        IConfigurationSection svc = configuration.GetSection("CafeteriaServices");
        BitwardenSecretsManagerOptions sm = allowRemoteSecrets
            ? ReadSecretsManagerOptions(configuration)
            : new BitwardenSecretsManagerOptions();
        bool servicesEnabled = svc.GetValue("Enabled", false);
        bool developmentDataEnabled = configuration.GetSection("DevelopmentData").GetValue("Enabled", false);
        string? messagingToken = allowRemoteSecrets && messaging.GetValue("Enabled", false) && secrets is not null
            ? await secrets.GetSecretAsync(ApplicationSecretNames.MessagingBearerToken, cancellationToken)
            : null;

        CafeteriaPersistenceOptions persistence = new()
        {
            LocalSqliteConnectionString = p["LocalSqliteConnectionString"] ?? "Data Source=cafeteria.local.db;Cache=Shared",
            TerminalId = p["TerminalId"] ?? Environment.MachineName
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
            Enabled = allowRemoteSecrets && messaging.GetValue("Enabled", false),
            Endpoint = messaging["Endpoint"] ?? string.Empty,
            BearerToken = messagingToken,
            WeeklySummaryDay = Enum.TryParse<DayOfWeek>(messaging["WeeklySummaryDay"], true, out DayOfWeek day) ? day : DayOfWeek.Monday,
            WeeklySummaryHourLocal = Math.Clamp(messaging.GetValue("WeeklySummaryHourLocal", 8), 0, 23)
        };

        string? serviceApiKey = null;
        if (allowRemoteSecrets && servicesEnabled)
        {
            string? configuredServiceApiKey = svc["ApiKey"];
            serviceApiKey = !string.IsNullOrWhiteSpace(configuredServiceApiKey)
                ? configuredServiceApiKey.Trim()
                : secrets is null ? null : await secrets.GetSecretAsync(ApplicationSecretNames.CafeteriaServicesApiKey, cancellationToken);
        }

        CafeteriaServicesOptions servicesOptions = new()
        {
            Enabled = servicesEnabled,
            BaseUrl = svc["BaseUrl"]?.Trim() ?? "http://localhost:5088",
            PollSeconds = Math.Max(5, svc.GetValue("PollSeconds", 15)),
            RequestTimeoutSeconds = Math.Max(5, svc.GetValue("RequestTimeoutSeconds", 10)),
            StartupRefreshTimeoutSeconds = Math.Max(5, svc.GetValue("StartupRefreshTimeoutSeconds", 12)),
            ApiKey = string.IsNullOrWhiteSpace(serviceApiKey) ? null : serviceApiKey.Trim(),
            RealtimeEnabled = svc.GetValue("RealtimeEnabled", true),
            HubPath = svc["HubPath"]?.Trim() ?? "/hubs/cafeteria",
            RealtimeReconnectSeconds = Math.Max(2, svc.GetValue("RealtimeReconnectSeconds", 5))
        };
        return new CafeteriaRuntimeOptions(persistence, logging, sm, healthOptions, messagingOptions, servicesOptions);
    }

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
