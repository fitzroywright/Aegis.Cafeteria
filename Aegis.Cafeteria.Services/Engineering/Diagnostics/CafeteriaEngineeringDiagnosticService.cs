using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Aegis.Engineering.Diagnostics;

public sealed class CafeteriaEngineeringDiagnosticService
{
    private readonly IDbContextFactory<ServiceDbContext> databaseFactory;
    private readonly IWorkforceReferenceSource workforce;
    private readonly EngineeringDiagnosticRunStore runStore;
    private readonly IHostEnvironment environment;

    public CafeteriaEngineeringDiagnosticService
    (
          IDbContextFactory<ServiceDbContext> databaseFactory
        , IWorkforceReferenceSource workforce
        , EngineeringDiagnosticRunStore runStore
        , IHostEnvironment environment
    )
    {
        this.databaseFactory = databaseFactory;
        this.workforce = workforce;
        this.runStore = runStore;
        this.environment = environment;
    }

    public async Task<EngineeringDiagnosticRun> RunAsync
    (
          EngineeringDiagnosticLevel level
        , string requestedBy
        , string? reason
        , CancellationToken cancellationToken = default
    )
    {
        ValidateLevel(level);

        if ((level == EngineeringDiagnosticLevel.Level2Repair || level == EngineeringDiagnosticLevel.Level1CriticalIntervention)
            && string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("Level 2 and Level 1 diagnostics require an engineering reason.", nameof(reason));
        }

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        List<EngineeringDiagnosticCheckResult> checks = [];
        checks.Add(await CheckServiceDatabaseAsync(cancellationToken));
        checks.Add(await CheckEligibleEmployeesAsync(cancellationToken));
        checks.Add(await CheckReportStatusAsync(cancellationToken));

        if ((int)level <= (int)EngineeringDiagnosticLevel.Level4Analysis)
        {
            checks.Add(await CheckCurrentMonthHolidaysAsync(cancellationToken));
            checks.Add(await CheckServiceEventBacklogAsync(cancellationToken));
        }

        if ((int)level <= (int)EngineeringDiagnosticLevel.Level3Verification)
        {
            checks.Add(await CheckRecentServiceActivityAsync(cancellationToken));
        }

        if (level == EngineeringDiagnosticLevel.Level2Repair)
        {
            checks.Add
            (
                new EngineeringDiagnosticCheckResult
                (
                      "repair-gate"
                    , "Repair authorization gate"
                    , EngineeringDiagnosticStatus.InterventionRequired
                    , "Evidence collection is complete. Automated destructive repair is disabled; an engineer must explicitly select and approve a repair action."
                    , reason
                )
            );
        }

        if (level == EngineeringDiagnosticLevel.Level1CriticalIntervention)
        {
            checks.Add
            (
                new EngineeringDiagnosticCheckResult
                (
                      "critical-intervention-gate"
                    , "Critical intervention gate"
                    , EngineeringDiagnosticStatus.InterventionRequired
                    , "Critical evidence collection is complete. Level 1 intervention requires explicit engineering approval and is never executed automatically by a diagnostic run."
                    , reason
                )
            );
        }

        EngineeringDiagnosticRun run = new
        (
              Guid.NewGuid()
            , level
            , "Aegis.Cafeteria"
            , this.environment.EnvironmentName
            , requestedBy
            , string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()
            , startedAt
            , DateTimeOffset.UtcNow
            , CalculateStatus(checks)
            , checks
        );

        await this.runStore.SaveAsync(run, cancellationToken);
        return run;
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckServiceDatabaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await this.databaseFactory.CreateDbContextAsync(cancellationToken);
            bool connected = await database.Database.CanConnectAsync(cancellationToken);
            return connected
                ? Passed("service-database", "Services database", "Services SQLite is reachable.")
                : Failed("service-database", "Services database", "Services SQLite could not be reached.");
        }
        catch (Exception exception)
        {
            return Failed("service-database", "Services database", exception.Message);
        }
    }


    private async Task<EngineeringDiagnosticCheckResult> CheckEligibleEmployeesAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ExternalCustomerSnapshot> customers = await this.workforce.GetCustomersAsync(cancellationToken);
            int eligible = customers.Count(customer => customer.IsEligibleForCafeteriaCredit);
            return eligible > 0
                ? Passed("eligible-employees", "Eligible employee source", $"FFP returned {eligible} eligible employee(s).", $"Total source rows={customers.Count}")
                : Failed("eligible-employees", "Eligible employee source", "FFP returned no cafeteria-eligible employees.");
        }
        catch (Exception exception)
        {
            return Failed("eligible-employees", "Eligible employee source", exception.Message);
        }
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckReportStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await this.databaseFactory.CreateDbContextAsync(cancellationToken);
            int pending = await database.Events.CountAsync(row => row.FfpPublishedAt == null, cancellationToken);
            int failed = await database.Events.CountAsync(row => row.FfpLastError != null, cancellationToken);
            DateTimeOffset? lastSuccessfulSync = await database.Events
                .Where(row => row.FfpPublishedAt != null)
                .OrderByDescending(row => row.FfpPublishedAt)
                .Select(row => row.FfpPublishedAt)
                .FirstOrDefaultAsync(cancellationToken);
            string? lastError = await database.Events
                .Where(row => row.FfpLastError != null)
                .OrderByDescending(row => row.FfpLastAttemptAt)
                .Select(row => row.FfpLastError)
                .FirstOrDefaultAsync(cancellationToken);

            CafeteriaReportStatus status = CafeteriaReportStatus.FromPublicationStatus(
                new ExternalPublicationStatus(true, pending, failed, lastSuccessfulSync, lastError));

            string lastSync = status.LastSuccessfulSync.HasValue
                ? status.LastSuccessfulSync.Value.ToLocalTime().ToString("yyyy-MM-dd h:mm tt")
                : "Not Available";
            string evidence = $"Report Status: {status.DisplayStatus}; Last Successful Sync: {lastSync}; Pending Sync Items={status.PendingSyncItems}; Failed Sync Items={status.FailedSyncItems}";

            return status.State == CafeteriaReportState.Complete
                ? Passed("report-status", "Cafeteria report status", "Report Status: Complete", evidence)
                : new EngineeringDiagnosticCheckResult(
                    "report-status",
                    "Cafeteria report status",
                    EngineeringDiagnosticStatus.Warning,
                    "Report Status: Data Incomplete",
                    evidence);
        }
        catch (Exception exception)
        {
            return Failed("report-status", "Cafeteria report status", exception.Message);
        }
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckCurrentMonthHolidaysAsync(CancellationToken cancellationToken)
    {
        try
        {
            DateTime today = DateTime.Today;
            IReadOnlyCollection<DateOnly> holidays = await this.workforce.GetHolidaysAsync(today.Year, today.Month, cancellationToken);
            return Passed("holiday-source", "Holiday source", $"Holiday source is reachable for {today:yyyy-MM}.", $"Holiday count={holidays.Count}");
        }
        catch (Exception exception)
        {
            return Failed("holiday-source", "Holiday source", exception.Message);
        }
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckServiceEventBacklogAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await this.databaseFactory.CreateDbContextAsync(cancellationToken);
            int pending = await database.Events.CountAsync(row => row.FfpPublishedAt == null, cancellationToken);
            int failed = await database.Events.CountAsync(row => row.FfpLastError != null, cancellationToken);

            if (failed > 0)
            {
                return new EngineeringDiagnosticCheckResult
                (
                      "ffp-publication"
                    , "FFP publication queue"
                    , EngineeringDiagnosticStatus.Warning
                    , $"{failed} event(s) have recorded FFP publication errors."
                    , $"Pending={pending}; Failed={failed}"
                );
            }

            return Passed("ffp-publication", "FFP publication queue", "No recorded FFP publication errors.", $"Pending={pending}");
        }
        catch (Exception exception)
        {
            return Failed("ffp-publication", "FFP publication queue", exception.Message);
        }
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckRecentServiceActivityAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using ServiceDbContext database = await this.databaseFactory.CreateDbContextAsync(cancellationToken);
            ServiceEventRow? latest = await database.Events.AsNoTracking().OrderByDescending(row => row.Sequence).FirstOrDefaultAsync(cancellationToken);

            if (latest is null)
            {
                return new EngineeringDiagnosticCheckResult
                (
                      "service-events"
                    , "Service event stream"
                    , EngineeringDiagnosticStatus.Warning
                    , "The service event stream is empty. This can be normal immediately after a Development reset."
                );
            }

            return Passed
            (
                  "service-events"
                , "Service event stream"
                , "Service event history is readable."
                , $"Latest sequence={latest.Sequence}; Occurred={latest.OccurredAt:O}; Kind={latest.Kind}"
            );
        }
        catch (Exception exception)
        {
            return Failed("service-events", "Service event stream", exception.Message);
        }
    }

    private static EngineeringDiagnosticStatus CalculateStatus(IReadOnlyCollection<EngineeringDiagnosticCheckResult> checks)
    {
        if (checks.Any(check => check.Status == EngineeringDiagnosticStatus.Failed))
        {
            return EngineeringDiagnosticStatus.Failed;
        }

        if (checks.Any(check => check.Status == EngineeringDiagnosticStatus.InterventionRequired))
        {
            return EngineeringDiagnosticStatus.InterventionRequired;
        }

        if (checks.Any(check => check.Status == EngineeringDiagnosticStatus.Warning))
        {
            return EngineeringDiagnosticStatus.Warning;
        }

        return EngineeringDiagnosticStatus.Passed;
    }

    private static void ValidateLevel(EngineeringDiagnosticLevel level)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Diagnostic level must be Level 5 through Level 1.");
        }
    }

    private static EngineeringDiagnosticCheckResult Passed(string checkId, string name, string summary, string? evidence = null)
    {
        return new EngineeringDiagnosticCheckResult(checkId, name, EngineeringDiagnosticStatus.Passed, summary, evidence);
    }

    private static EngineeringDiagnosticCheckResult Failed(string checkId, string name, string summary)
    {
        return new EngineeringDiagnosticCheckResult(checkId, name, EngineeringDiagnosticStatus.Failed, summary);
    }
}
