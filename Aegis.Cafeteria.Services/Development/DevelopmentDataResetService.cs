namespace Aegis.Cafeteria.Services.Development;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class DevelopmentDataResetService
{
    private readonly IDevelopmentDataResetGateway gateway;
    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly ServiceEventStore events;
    private readonly ILogger<DevelopmentDataResetService> logger;

    public DevelopmentDataResetService
    (
          IDevelopmentDataResetGateway gateway
        , IDbContextFactory<ServiceDbContext> factory
        , ServiceEventStore events
        , ILogger<DevelopmentDataResetService> logger
    )
    {
        this.gateway = gateway;
        this.factory = factory;
        this.events = events;
        this.logger = logger;
    }

    public async Task<DevelopmentResetResult> ResetAsync
    (
          string resetBy
        , CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(resetBy))
        {
            throw new ArgumentException("A management identity is required.", nameof(resetBy));
        }

        DateTimeOffset resetAt = DateTimeOffset.Now;
        bool preserveProducts = true;
        bool preserveFundingPolicy = true;

        DevelopmentDataResetResult ffp = await this.gateway.ResetAsync
        (
              resetBy.Trim()
            , resetAt
            , cancellationToken
        );

        Guid operationId = Guid.NewGuid();
        DevelopmentResetServiceEvent payload = new
        (
              operationId
            , resetBy.Trim()
            , resetAt
            , preserveProducts
            , preserveFundingPolicy
        );

        CafeteriaServiceEventEnvelope resetEvent = await this.events.AppendAsync
        (
              operationId
            , CafeteriaServiceEventKind.DevelopmentReset
            , 0
            , resetAt
            , payload
            , cancellationToken
        );

        await this.events.RecordFfpAttemptAsync
        (
              operationId
            , null
            , true
            , cancellationToken
        );

        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        int deletedEvents = await database.Events
            .Where(serviceEvent => serviceEvent.OperationId != operationId)
            .ExecuteDeleteAsync(cancellationToken);

        int deletedPolicies = await database.CustomerCreditPolicies.ExecuteDeleteAsync(cancellationToken);
        int deletedDispatches = await database.ScheduledDispatches.ExecuteDeleteAsync(cancellationToken);
        int deletedFundingHistory = await database.FundingPolicyHistory.ExecuteDeleteAsync(cancellationToken);

        this.logger.LogWarning
        (
              "Development Cafeteria data reset by {Actor}: FFP ledger {LedgerCount}, accounts {AccountCount}, service events {EventCount}"
            , resetBy
            , ffp.DeletedLedgerEntries
            , ffp.ResetEmployeeAccounts
            , deletedEvents
        );

        return new DevelopmentResetResult
        (
              resetEvent.Sequence
            , resetAt
            , resetBy.Trim()
            , ffp.DeletedLedgerEntries
            , ffp.ResetEmployeeAccounts
            , deletedEvents
            , deletedPolicies
            , deletedDispatches
            , deletedFundingHistory
        );
    }
}

public sealed record DevelopmentResetResult
(
      long ResetSequence
    , DateTimeOffset ResetAt
    , string ResetBy
    , int DeletedFfpLedgerEntries
    , int ResetFfpEmployeeAccounts
    , int DeletedServiceEvents
    , int DeletedCustomerCreditPolicies
    , int DeletedScheduledDispatches
    , int DeletedFundingPolicyHistory
);
