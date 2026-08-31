using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Aegis.Cafeteria.Services.Engineering;

public sealed class CafeteriaEngineeringDiagnosticService
{
    private readonly IDbContextFactory<ServiceDbContext> databaseFactory;
    private readonly IWorkforceReferenceSource workforce;
    private readonly CafeteriaFundingPolicyStore fundingPolicy;
    private readonly EngineeringDiagnosticRunStore runStore;
    private readonly IHostEnvironment environment;

    public CafeteriaEngineeringDiagnosticService
    (
          IDbContextFactory<ServiceDbContext> databaseFactory
        , IWorkforceReferenceSource workforce
        , CafeteriaFundingPolicyStore fundingPolicy
        , EngineeringDiagnosticRunStore runStore
        , IHostEnvironment environment
    )
    {
        this.databaseFactory = databaseFactory;
        this.workforce = workforce;
        this.fundingPolicy = fundingPolicy;
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
        checks.Add(await CheckFundingPolicyAsync(cancellationToken));
        checks.Add(await CheckEligibleEmployeesAsync(cancellationToken));

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
                    , "Evidence collected. Automated destructive repair is disabled; an engineer must select and approve a repair action."
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
                    , "Critical evidence collected. Level 1 actions require explicit engineering approval and are never executed automatically by a diagnostic run."
                    , reason
                )
            );
        }

        EngineeringDiagnosticStatus status = CalculateStatus(checks);
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
            , status
            , checks
        );

        await this.runStore.SaveAsync(run, cancellationToken);
        return run;
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckServiceDatabaseAsync
    (
        CancellationToken cancellationToken
    )
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

    private async Task<EngineeringDiagnosticCheckResult> CheckFundingPolicyAsync
    (
        CancellationToken cancellationToken
    )
    {
        try
        {
            CafeteriaFundingPolicySnapshot policy = await this.fundingPolicy.GetFundingPolicyAsync(cancellationToken);
            string evidence = $"Daily credit={policy.DailyCreditLimitRate:0.00}; Daily subsidy={policy.DailyCompanySubsidyRate:0.00}; Updated={policy.UpdatedAt:O}";
            return Passed("funding-policy", "Funding policy", "Funding policy is configured.", evidence);
        }
        catch (Exception exception)
        {
            return Failed("funding-policy", "Funding policy", exception.Message);
        }
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckEligibleEmployeesAsync
    (
        CancellationToken cancellationToken
    )
    {
        try
        {
            IReadOnlyList<ExternalCustomerSnapshot> customers = await this.workforce.GetCustomersAsync(cancellationToken);
            int eligible = customers.Count(customer => customer.IsEligibleForCafeteriaCredit);

            if (eligible == 0)
            {
                return Failed("eligible-employees", "Eligible employee source", "FFP returned no cafeteria-eligible employees.");
            }

            return Passed("eligible-employees", "Eligible employee source", $"FFP returned {eligible} eligible employee(s).", $"Total source rows={customers.Count}");
        }
        catch (Exception exception)
        {
            return Failed("eligible-employees", "Eligible employee source", exception.Message);
        }
    }

    private async Task<EngineeringDiagnosticCheckResult> CheckCurrentMonthHolidaysAsync
    (
        CancellationToken cancellationToken
    )
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

    private async Task<EngineeringDiagnosticCheckResult> CheckServiceEventBacklogAsync
    (
        CancellationToken cancellationToken
    )
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

    private async Task<EngineeringDiagnosticCheckResult> CheckRecentServiceActivityAsync
    (
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using ServiceDbContext database = await this.databaseFactory.CreateDbContextAsync(cancellationToken);
            ServiceEventRow? latest = await database.Events
                .AsNoTracking()
                .OrderByDescending(row => row.Sequence)
                .FirstOrDefaultAsync(cancellationToken);

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

    private static EngineeringDiagnosticStatus CalculateStatus
    (
        IReadOnlyCollection<EngineeringDiagnosticCheckResult> checks
    )
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

    private static EngineeringDiagnosticCheckResult Passed
    (
          string checkId
        , string name
        , string summary
        , string? evidence = null
    )
    {
        return new EngineeringDiagnosticCheckResult(checkId, name, EngineeringDiagnosticStatus.Passed, summary, evidence);
    }

    private static EngineeringDiagnosticCheckResult Failed
    (
          string checkId
        , string name
        , string summary
    )
    {
        return new EngineeringDiagnosticCheckResult(checkId, name, EngineeringDiagnosticStatus.Failed, summary);
    }
}
