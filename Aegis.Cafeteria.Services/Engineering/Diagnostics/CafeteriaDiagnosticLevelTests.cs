using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Reporting;
using Common.Diagnostics;
using Common.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace Aegis.Cafeteria.Services.Engineering.Diagnostics;

public static class CafeteriaDiagnosticModules
{
    public const string Services = "Aegis.Cafeteria.Services";
    public const string Pos = "Aegis.Cafeteria.POS";
    public const string PosUi = "Aegis.Cafeteria.POS.UI";

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Services,
            Pos,
            PosUi
        };
}

internal static class CafeteriaDiagnosticIds
{
    internal const string DatabaseConnectivity = "CAFETERIA.SERVICES.DATABASE.CONNECTIVITY";
    internal const string DatabaseIntegrity = "CAFETERIA.SERVICES.DATABASE.INTEGRITY";
    internal const string Configuration = "CAFETERIA.SERVICES.CONFIGURATION";
    internal const string RuntimeIdentity = "CAFETERIA.SERVICES.RUNTIME.IDENTITY";
    internal const string DiagnosticStore = "CAFETERIA.SERVICES.DIAGNOSTIC-STORE";
    internal const string WorkforceSource = "CAFETERIA.SERVICES.WORKFORCE-SOURCE";
    internal const string HolidaySource = "CAFETERIA.SERVICES.HOLIDAY-SOURCE";
    internal const string SecretsCredential = "CAFETERIA.SERVICES.SECRETS.CREDENTIAL";
    internal const string PublicationQueue = "CAFETERIA.SERVICES.PUBLICATION-QUEUE";
    internal const string CafeteriaLedger = "CAFETERIA.SERVICES.CAFETERIA-LEDGER";
    internal const string Messaging = "CAFETERIA.SERVICES.MESSAGING";
    internal const string FundingPolicy = "CAFETERIA.SERVICES.FUNDING-POLICY";
    internal const string CustomerLookup = "CAFETERIA.SERVICES.CUSTOMER-LOOKUP";
    internal const string RecoveryReadiness = "CAFETERIA.SERVICES.RECOVERY-READINESS";
    internal const string ExhaustiveConsistency = "CAFETERIA.SERVICES.EXHAUSTIVE-CONSISTENCY";
    internal const string OutboundEmailQueue = "CAFETERIA.SERVICES.OUTBOUND-EMAIL-QUEUE";
    internal const string ReportDeliveryConfiguration = "CAFETERIA.SERVICES.REPORT-DELIVERY-CONFIGURATION";
    internal const string AuditTrailIntegrity = "CAFETERIA.SERVICES.AUDIT-TRAIL-INTEGRITY";
}

public sealed class CafeteriaDatabaseConnectivityDiagnosticLevelTest(
    IDbContextFactory<ServiceDbContext> databaseFactory) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.DatabaseConnectivity;
    public string Name => "Services database connectivity";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            bool connected = await database.Database.CanConnectAsync(cancellationToken);
            return connected
                ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "Cafeteria Services database is reachable.")
                : EngineeringDiagnosticPolicy.Failed(TestId, Name, "Cafeteria Services database could not be reached.");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaDatabaseIntegrityDiagnosticLevelTest(
    IDbContextFactory<ServiceDbContext> databaseFactory) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.DatabaseIntegrity;
    public string Name => "Services database integrity";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            string? connectionString = database.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(connectionString))
                return EngineeringDiagnosticPolicy.Warning(TestId, Name, "Database connection string is not available for an integrity probe.");

            var builder = new SqliteConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
                return EngineeringDiagnosticPolicy.Passed(TestId, Name, "Database is in-memory; file integrity probe is not applicable.");

            string path = Path.GetFullPath(builder.DataSource);
            if (!File.Exists(path))
                return EngineeringDiagnosticPolicy.Failed(TestId, Name, "Database file is missing.", path);

            FileInfo info = new(path);
            return info.Length > 0
                ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "Database file is present and non-empty.", $"Size={info.Length} bytes")
                : EngineeringDiagnosticPolicy.Warning(TestId, Name, "Database file exists but is empty.", path);
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaConfigurationDiagnosticLevelTest(
    IConfiguration configuration) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.Configuration;
    public string Name => "Required service configuration";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string[] keys =
        [
            "Aegis:PublicUrl",
            "Aegis:Configuration:Url",
            "Aegis:Diagnostics:Url",
            "Aegis:Diagnostics:RequestCredentialSecretName",
            "ServicePersistence:ConnectionString",
            "Service:Identity"
        ];

        string[] missing = keys.Where(key => string.IsNullOrWhiteSpace(configuration[key])).ToArray();
        return Task.FromResult(missing.Length == 0
            ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "Required Cafeteria Services configuration is present.")
            : EngineeringDiagnosticPolicy.Warning(TestId, Name, "One or more required configuration values are unresolved.", $"Missing={string.Join(",", missing)}"));
    }
}

public sealed class CafeteriaRuntimeIdentityDiagnosticLevelTest(
    IConfiguration configuration,
    IHostEnvironment environment) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.RuntimeIdentity;
    public string Name => "Runtime identity";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string instanceId = configuration["Service:Identity"]?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(instanceId))
            return Task.FromResult(EngineeringDiagnosticPolicy.Warning(TestId, Name, "Service:Identity is not explicitly configured.", $"Environment={environment.EnvironmentName}; Machine={Environment.MachineName}"));

        return Task.FromResult(EngineeringDiagnosticPolicy.Passed(
            TestId,
            Name,
            "Stable Cafeteria Services identity is configured.",
            $"Instance={instanceId}; Environment={environment.EnvironmentName}; Machine={Environment.MachineName}"));
    }
}

public sealed class CafeteriaDiagnosticStoreIntegrityDiagnosticLevelTest(
    IDiagnosticLevelRunStore store) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.DiagnosticStore;
    public string Name => "Diagnostic run store";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await store.InitializeAsync(cancellationToken);
            await store.QueryAsync(new DiagnosticLevelHistoryQuery(Take: 1), cancellationToken);
            return EngineeringDiagnosticPolicy.Passed(TestId, Name, "Diagnostic run store initialized and is readable.");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaWorkforceSourceDiagnosticLevelTest(
    IWorkforceReferenceSource workforce) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.WorkforceSource;
    public string Name => "External workforce source";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ExternalCustomerSnapshot> customers = await workforce.GetCustomersAsync(cancellationToken);
            int active = customers.Count(x => x.IsActive);
            int eligible = customers.Count(x => x.IsActive && x.IsEligibleForCafeteriaCredit);
            return customers.Count > 0
                ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "External workforce source is reachable.", $"Rows={customers.Count}; Active={active}; Eligible={eligible}")
                : EngineeringDiagnosticPolicy.Warning(TestId, Name, "External workforce source is reachable but returned no records.");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaHolidaySourceDiagnosticLevelTest(
    IWorkforceReferenceSource workforce) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.HolidaySource;
    public string Name => "Holiday reference source";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            DateTime today = DateTime.Today;
            IReadOnlyCollection<DateOnly> holidays = await workforce.GetHolidaysAsync(today.Year, today.Month, cancellationToken);
            return EngineeringDiagnosticPolicy.Passed(TestId, Name, "Holiday reference source is reachable.", $"Month={today:yyyy-MM}; Count={holidays.Count}");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaSecretsCredentialDiagnosticLevelTest(
    ISecretProvider secrets,
    IConfiguration configuration) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.SecretsCredential;
    public string Name => "Diagnostic credential availability";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        string? secretName = configuration["Aegis:Diagnostics:RequestCredentialSecretName"]?.Trim();
        if (string.IsNullOrWhiteSpace(secretName))
            return EngineeringDiagnosticPolicy.Warning(TestId, Name, "DiagnosticLevel request credential name is not configured.");

        try
        {
            string? credential = await secrets.GetAsync(secretName, cancellationToken);
            return !string.IsNullOrWhiteSpace(credential)
                ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "DiagnosticLevel request credential can be resolved.")
                : EngineeringDiagnosticPolicy.Failed(TestId, Name, "Configured DiagnosticLevel request credential is missing.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, "DiagnosticLevel request credential could not be resolved.", exception.GetType().Name);
        }
    }
}

public sealed class CafeteriaPublicationQueueDiagnosticLevelTest(
    IDbContextFactory<ServiceDbContext> databaseFactory) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.PublicationQueue;
    public string Name => "External publication queue";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            int pending = await database.Events.AsNoTracking().CountAsync(x => x.ExternalPublishedAt == null, cancellationToken);
            int failed = await database.Events.AsNoTracking().CountAsync(x => x.PublicationLastError != null, cancellationToken);

            if (failed > 0)
                return EngineeringDiagnosticPolicy.Warning(TestId, Name, "External publication queue contains failed items.", $"Pending={pending}; Failed={failed}");

            return EngineeringDiagnosticPolicy.Passed(TestId, Name, "External publication queue has no recorded failures.", $"Pending={pending}; Failed=0");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaLedgerDiagnosticLevelTest(
    CafeteriaLedgerQuery ledger) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.CafeteriaLedger;
    public string Name => "Cafeteria financial ledger";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            DateTime today = DateTime.Today;
            IReadOnlyList<CafeteriaLedgerLine> lines = await ledger.GetMonthAsync(
                today.Year,
                today.Month,
                cancellationToken);

            return EngineeringDiagnosticPolicy.Passed(
                TestId,
                Name,
                "Cafeteria-owned financial ledger is readable.",
                $"Month={today:yyyy-MM}; Entries={lines.Count}");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(
                TestId,
                Name,
                exception.Message);
        }
    }
}

public sealed class CafeteriaMessagingDiagnosticLevelTest(
    IConfiguration configuration,
    IEnumerable<Common.Messaging.IExternalMessageChannel> channels) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.Messaging;
    public string Name => "Common.Messaging provider verification";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level3Verification;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        EmailDeliveryOptions options = configuration.GetSection("EmailDelivery").Get<EmailDeliveryOptions>() ?? new();
        string? error = options.ConfigurationError();
        if (error is not null)
        {
            return options.Enabled
                ? EngineeringDiagnosticPolicy.Failed(TestId, Name, error)
                : EngineeringDiagnosticPolicy.Warning(TestId, Name, error);
        }

        Common.Messaging.IExternalMessageChannelDiagnostic? diagnostic = channels
            .OfType<Common.Messaging.IExternalMessageChannelDiagnostic>()
            .FirstOrDefault(channel => channel.Channel == Common.Messaging.MessageChannel.MsEmail);

        if (diagnostic is null)
        {
            return EngineeringDiagnosticPolicy.Failed(
                TestId,
                Name,
                "Microsoft Graph email is configured but the Common.Messaging channel does not expose live provider verification.");
        }

        Common.Messaging.MessagingProviderHealth result = await diagnostic.VerifyProviderAsync(cancellationToken).ConfigureAwait(false);
        string evidence = $"Provider={result.Provider}; Configured={result.Configured}; Reachable={result.Reachable}; State={result.State}; LatencyMs={result.Latency?.TotalMilliseconds:F0}; Reason={result.Reason ?? "none"}";
        return result.Configured && result.Reachable
            ? EngineeringDiagnosticPolicy.Passed(
                TestId,
                Name,
                "Common.Messaging authenticated successfully and the configured Microsoft Graph sender mailbox is accessible.",
                evidence)
            : EngineeringDiagnosticPolicy.Failed(
                TestId,
                Name,
                "Common.Messaging Microsoft Graph provider verification failed.",
                evidence);
    }
}

public sealed class CafeteriaFundingPolicyDiagnosticLevelTest(
    ICafeteriaPolicyProvider policyProvider) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.FundingPolicy;
    public string Name => "Funding policy";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            DateTime today = DateTime.Today;
            CafeteriaPolicySnapshot policy = await policyProvider.GetPolicyAsync(
                today.Year,
                today.Month,
                cancellationToken: cancellationToken);

            policy.Validate();

            return EngineeringDiagnosticPolicy.Passed(
                TestId,
                Name,
                "Authoritative Cafeteria policy is available and valid.",
                $"Period={policy.Year:D4}-{policy.Month:D2}; Version={policy.Version}; WorkingDays={policy.WorkingDays.Count}");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaCustomerLookupDiagnosticLevelTest(
    IWorkforceReferenceSource workforce) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.CustomerLookup;
    public string Name => "Customer lookup path";
    public string Owner => CafeteriaDiagnosticModules.Pos;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level3Verification;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ExternalCustomerSnapshot> customers = await workforce.GetCustomersAsync(cancellationToken);
            ExternalCustomerSnapshot? sample = customers.FirstOrDefault(x => x.IsActive);
            if (sample is null)
                return EngineeringDiagnosticPolicy.Warning(TestId, Name, "No active customer is available for a non-destructive lookup verification.");

            bool resolvable = sample.CustomerId > 0 && !string.IsNullOrWhiteSpace(sample.Name);
            return resolvable
                ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "Customer lookup data contains a resolvable active record.", $"CustomerId={sample.CustomerId}")
                : EngineeringDiagnosticPolicy.Failed(TestId, Name, "Customer lookup returned an active record without a usable identifier or name.");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaRecoveryReadinessDiagnosticLevelTest(
    IConfiguration configuration) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.RecoveryReadiness;
    public string Name => "Publication recovery readiness";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level2Repair;
    public bool IsDestructive => false;

    public Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        int retrySeconds = configuration.GetValue("ExternalPublication:RetryIntervalSeconds", 0);
        int poisonLimit = configuration.GetValue("ExternalPublication:PoisonMessageAttemptLimit", 0);
        return Task.FromResult(retrySeconds > 0 && poisonLimit > 0
            ? EngineeringDiagnosticPolicy.Passed(TestId, Name, "External publication recovery controls are configured.", $"RetryIntervalSeconds={retrySeconds}; PoisonMessageAttemptLimit={poisonLimit}")
            : EngineeringDiagnosticPolicy.Warning(TestId, Name, "External publication recovery controls are incomplete."));
    }
}

public sealed class CafeteriaExhaustiveConsistencyDiagnosticLevelTest(
    IDbContextFactory<ServiceDbContext> databaseFactory,
    IWorkforceReferenceSource workforce,
    ICafeteriaPolicyProvider policyProvider) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.ExhaustiveConsistency;
    public string Name => "Cafeteria service consistency certification";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level1CriticalIntervention;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            if (!await database.Database.CanConnectAsync(cancellationToken))
                return EngineeringDiagnosticPolicy.Failed(TestId, Name, "Services database is unavailable.");

            DateTime today = DateTime.Today;
            CafeteriaPolicySnapshot policy = await policyProvider.GetPolicyAsync(
                today.Year,
                today.Month,
                cancellationToken: cancellationToken);
            IReadOnlyList<ExternalCustomerSnapshot> customers = await workforce.GetCustomersAsync(cancellationToken);
            int eventCount = await database.Events.AsNoTracking().CountAsync(cancellationToken);

            return EngineeringDiagnosticPolicy.Passed(
                TestId,
                Name,
                "Core service state, authoritative policy and workforce reference data can be read together without mutation.",
                $"Customers={customers.Count}; Events={eventCount}; PolicyVersion={policy.Version}");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}


public sealed class CafeteriaOutboundEmailQueueDiagnosticLevelTest(
    IDbContextFactory<ServiceDbContext> databaseFactory) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.OutboundEmailQueue;
    public string Name => "Outbound email queue";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level5Scan;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await databaseFactory.CreateDbContextAsync(cancellationToken);
            OutboundEmailRow[] pending = await database.OutboundEmails
                .AsNoTracking()
                .Where(row => row.SentAt == null)
                .ToArrayAsync(cancellationToken);

            int failed = pending.Count(row => !string.IsNullOrWhiteSpace(row.LastError));
            int retrying = pending.Count(row => row.AttemptCount > 0);

            return failed > 0
                ? EngineeringDiagnosticPolicy.Warning(
                    TestId,
                    Name,
                    "Outbound email queue contains messages with recorded delivery errors.",
                    $"Pending={pending.Length}; Retrying={retrying}; WithErrors={failed}")
                : EngineeringDiagnosticPolicy.Passed(
                    TestId,
                    Name,
                    "Outbound email queue has no recorded delivery errors.",
                    $"Pending={pending.Length}; Retrying={retrying}");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaReportDeliveryConfigurationDiagnosticLevelTest(
    Aegis.Cafeteria.Services.ScheduledEmailOptions options,
    IDbContextFactory<ServiceDbContext> databaseFactory) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.ReportDeliveryConfiguration;
    public string Name => "Report delivery configuration";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            NotificationPreferenceStore preferences = new(databaseFactory);
            ManagerReportSettings manager = await preferences.GetManagerAsync(cancellationToken);

            List<string> issues = [];
            if (options.DailySalesReportEnabled && string.IsNullOrWhiteSpace(manager.EmailAddress))
                issues.Add("Daily Sales is enabled but no manager email is configured.");
            if (manager.WeeklySummaryEnabled && string.IsNullOrWhiteSpace(manager.EmailAddress))
                issues.Add("Weekly manager summary is enabled but no manager email is configured.");
            if (options.PayrollNotificationEnabled && options.PayrollRecipients.Length == 0)
                issues.Add("Employee Amount Due notification is enabled but no payroll recipients are configured.");

            return issues.Count == 0
                ? EngineeringDiagnosticPolicy.Passed(
                    TestId,
                    Name,
                    "Scheduled cafeteria report delivery has the required recipient configuration.",
                    $"DailySales={options.DailySalesReportEnabled}; WeeklyManager={manager.WeeklySummaryEnabled}; Payroll={options.PayrollNotificationEnabled}; PayrollRecipients={options.PayrollRecipients.Length}")
                : EngineeringDiagnosticPolicy.Warning(
                    TestId,
                    Name,
                    "One or more scheduled report delivery paths require configuration.",
                    string.Join(" ", issues));
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

public sealed class CafeteriaAuditTrailIntegrityDiagnosticLevelTest(
    IDbContextFactory<ServiceDbContext> databaseFactory) : IDiagnosticLevelLocalTest
{
    public string TestId => CafeteriaDiagnosticIds.AuditTrailIntegrity;
    public string Name => "Audit trail integrity";
    public string Owner => CafeteriaDiagnosticModules.Services;
    public EngineeringDiagnosticLevel Level => EngineeringDiagnosticLevel.Level4Analysis;
    public bool IsDestructive => false;

    public async Task<EngineeringDiagnosticCheckResult> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            AuditIntegrityResult result = await new AuditTrailService(databaseFactory)
                .VerifyIntegrityAsync(cancellationToken);

            return result.IsValid
                ? EngineeringDiagnosticPolicy.Passed(
                    TestId,
                    Name,
                    "Cafeteria audit hash chain is valid.",
                    $"VerifiedRows={result.VerifiedRows}; LegacyRows={result.LegacyRows}")
                : EngineeringDiagnosticPolicy.Failed(
                    TestId,
                    Name,
                    $"{result.Error ?? "Audit trail integrity verification failed."} FirstInvalidAuditId={result.FirstInvalidAuditId}; VerifiedRows={result.VerifiedRows}; LegacyRows={result.LegacyRows}");
        }
        catch (Exception exception)
        {
            return EngineeringDiagnosticPolicy.Failed(TestId, Name, exception.Message);
        }
    }
}

