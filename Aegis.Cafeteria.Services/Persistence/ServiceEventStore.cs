namespace Aegis.Cafeteria.Services.Persistence;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Realtime;

using Microsoft.EntityFrameworkCore;

using System.Reflection;
using System.Text.Json;

public sealed class ServiceEventStore
{
    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly ServiceEventRealtimePublisher realtimePublisher;
    private readonly AuditTrailService auditTrail;
    private readonly CafeteriaOperationsFlowPublisher operationsFlowPublisher;

    public ServiceEventStore
    (
          IDbContextFactory<ServiceDbContext> factory
        , ServiceEventRealtimePublisher realtimePublisher
        , CafeteriaOperationsFlowPublisher operationsFlowPublisher
    )
    {
        this.factory = factory;
        this.realtimePublisher = realtimePublisher;
        this.operationsFlowPublisher = operationsFlowPublisher;
        this.auditTrail = new AuditTrailService(factory);
    }

    public async Task<CafeteriaServiceEventEnvelope> AppendAsync<T>
    (
          Guid operationId
        , CafeteriaServiceEventKind kind
        , int customerId
        , DateTimeOffset occurredAt
        , T payload
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        ServiceEventRow? existing = await database.Events
            .AsNoTracking()
            .SingleOrDefaultAsync
            (
                  serviceEvent => serviceEvent.OperationId == operationId
                , cancellationToken
            );

        if (existing is not null)
        {
            return ToEnvelope(existing);
        }

        ServiceEventRow row = new()
        {
              OperationId = operationId
            , Kind = (int)kind
            , CustomerId = customerId
            , OccurredAt = occurredAt
            , PayloadJson = JsonSerializer.Serialize(payload)
            , CreatedAt = DateTimeOffset.UtcNow
        };

        database.Events.Add(row);
        await database.SaveChangesAsync(cancellationToken);

        CafeteriaServiceEventEnvelope envelope = ToEnvelope(row);

        await this.realtimePublisher.PublishAsync
        (
              envelope
            , cancellationToken
        );

        try
        {
            await this.auditTrail.RecordAsync
            (
                  ResolveActor(payload)
                , kind.ToString()
                , "CafeteriaServiceEvent"
                , row.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                , customerId
                , operationId
                , "ServiceEventStore"
                , after: payload
                , detail: $"Service event {kind} created."
                , cancellationToken: cancellationToken
            );
        }
        catch
        {
            // The business event has already been durably committed. Audit transport/storage
            // failures must not roll back or duplicate a financial operation.
        }

        this.operationsFlowPublisher.TryEnqueueCommitted(
            operationId,
            kind.ToString(),
            row.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            occurredAt);

        return envelope;
    }

    public async Task<IReadOnlyList<CafeteriaServiceEventEnvelope>> GetAfterAsync
    (
          long afterSequence
        , int take
        , CancellationToken cancellationToken = default
    )
    {
        try
        {
            await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

            List<ServiceEventRow> rows = await database.Events
                .AsNoTracking()
                .Where(serviceEvent => serviceEvent.Sequence > afterSequence)
                .OrderBy(serviceEvent => serviceEvent.Sequence)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync(cancellationToken);

            return rows
                .Select(ToEnvelope)
                .ToArray();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return [];
        }
    }

    public async Task<DateTimeOffset?> GetLatestDevelopmentResetAtAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        return await database.Events
            .AsNoTracking()
            .Where(serviceEvent => serviceEvent.Kind == (int)CafeteriaServiceEventKind.DevelopmentReset)
            .OrderByDescending(serviceEvent => serviceEvent.Sequence)
            .Select(serviceEvent => (DateTimeOffset?)serviceEvent.OccurredAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdditionalCreditApproval>> GetAdditionalCreditApprovalsAsync
    (
          int customerId
        , int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        List<ServiceEventRow> candidates = await database.Events
            .AsNoTracking()
            .Where
            (
                serviceEvent =>
                       serviceEvent.CustomerId == customerId
                    && serviceEvent.Kind == (int)CafeteriaServiceEventKind.AdditionalCredit
            )
            .ToListAsync(cancellationToken);

        List<ServiceEventRow> rows = candidates
            .Where
            (
                serviceEvent =>
                       serviceEvent.OccurredAt.Year == year
                    && serviceEvent.OccurredAt.Month == month
            )
            .OrderBy(serviceEvent => serviceEvent.Sequence)
            .ToList();

        return rows
            .Select
            (
                serviceEvent => JsonSerializer.Deserialize<AdditionalCreditServiceEvent>
                (
                    serviceEvent.PayloadJson
                )
            )
            .Where(additionalCredit => additionalCredit is not null)
            .Select
            (
                additionalCredit => new AdditionalCreditApproval
                (
                      additionalCredit!.OperationId
                    , additionalCredit.CustomerId
                    , additionalCredit.Amount
                    , additionalCredit.ApprovedBy
                    , additionalCredit.ApprovedAt
                    , additionalCredit.Reason
                )
            )
            .ToArray();
    }

    public async Task<decimal> GetAdditionalCreditTotalAsync
    (
          int customerId
        , int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<AdditionalCreditApproval> approvals = await GetAdditionalCreditApprovalsAsync
        (
              customerId
            , year
            , month
            , cancellationToken
        );

        return approvals.Sum(approval => approval.Amount);
    }

    public async Task<CafeteriaServiceEventEnvelope?> GetByOperationAsync
    (
          Guid operationId
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        ServiceEventRow? row = await database.Events
            .AsNoTracking()
            .SingleOrDefaultAsync
            (
                  serviceEvent => serviceEvent.OperationId == operationId
                , cancellationToken
            );

        return row is null
            ? null
            : ToEnvelope(row);
    }

    public async Task<bool> HasOperationAsync
    (
          Guid operationId
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        return await database.Events.AnyAsync
        (
              serviceEvent => serviceEvent.OperationId == operationId
            , cancellationToken
        );
    }

    public async Task<IReadOnlyDictionary<int, MonthlyAllocationServiceEvent>> GetCurrentMonthlyAllocationsAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        List<ServiceEventRow> rows = await database.Events
            .AsNoTracking()
            .Where
            (
                serviceEvent =>
                       serviceEvent.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocation
                    || serviceEvent.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocationRecalculation
            )
            .OrderBy(serviceEvent => serviceEvent.Sequence)
            .ToListAsync(cancellationToken);

        Dictionary<int, MonthlyAllocationServiceEvent> allocations = [];

        foreach (ServiceEventRow row in rows)
        {
            MonthlyAllocationServiceEvent? allocation = null;

            if (row.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocation)
            {
                allocation = JsonSerializer.Deserialize<MonthlyAllocationServiceEvent>(row.PayloadJson);
            }
            else if (row.Kind == (int)CafeteriaServiceEventKind.MonthlyAllocationRecalculation)
            {
                MonthlyAllocationRecalculationServiceEvent? recalculation = JsonSerializer.Deserialize<MonthlyAllocationRecalculationServiceEvent>(row.PayloadJson);
                allocation = recalculation?.ReplacementAllocation;
            }

            if (allocation is not null && allocation.Year == year && allocation.Month == month)
            {
                allocations[allocation.CustomerId] = allocation;
            }
        }

        return allocations;
    }

    public async Task<IReadOnlyList<CafeteriaServiceEventEnvelope>> GetPendingFfpPublicationAsync
    (
          int take
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        List<ServiceEventRow> rows = await database.Events
            .AsNoTracking()
            .Where(serviceEvent => serviceEvent.FfpPublishedAt == null)
            .OrderBy(serviceEvent => serviceEvent.Sequence)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return rows
            .Select(ToEnvelope)
            .ToArray();
    }

    public async Task RecordFfpAttemptAsync
    (
          Guid operationId
        , string? error
        , bool succeeded
        , CancellationToken cancellationToken = default
    )
    {
        await using ServiceDbContext database = await this.factory.CreateDbContextAsync(cancellationToken);

        ServiceEventRow row = await database.Events.SingleAsync
        (
              serviceEvent => serviceEvent.OperationId == operationId
            , cancellationToken
        );

        row.FfpAttemptCount++;
        row.FfpLastAttemptAt = DateTimeOffset.UtcNow;
        row.FfpLastError = succeeded
            ? null
            : error;

        if (succeeded)
        {
            row.FfpPublishedAt = DateTimeOffset.UtcNow;
        }

        await database.SaveChangesAsync(cancellationToken);
    }

    private static string ResolveActor<T>(T payload)
    {
        if (payload is null)
        {
            return "SYSTEM";
        }

        Type type = payload.GetType();
        foreach (string propertyName in new[] { "ApprovedBy", "ProcessedBy", "UpdatedBy", "Actor", "UserName", "CashierUserId" })
        {
            PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            string? value = property?.GetValue(payload)?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "SYSTEM";
    }

    private static CafeteriaServiceEventEnvelope ToEnvelope
    (
        ServiceEventRow row
    )
    {
        return new CafeteriaServiceEventEnvelope
        (
              row.Sequence
            , row.OperationId
            , (CafeteriaServiceEventKind)row.Kind
            , row.CustomerId
            , row.OccurredAt
            , row.PayloadJson
        );
    }
}
