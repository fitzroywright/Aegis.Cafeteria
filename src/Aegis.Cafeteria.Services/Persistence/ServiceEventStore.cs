namespace Aegis.Cafeteria.Services.Persistence;

using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed class ServiceEventStore(IDbContextFactory<ServiceDbContext> factory)
{
    public async Task<CafeteriaServiceEventEnvelope> AppendAsync<T>(Guid operationId, CafeteriaServiceEventKind kind, int customerId, DateTimeOffset occurredAt, T payload, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);
        if (existing is not null) return ToEnvelope(existing);
        var row = new ServiceEventRow
        {
            OperationId=operationId, Kind=(int)kind, CustomerId=customerId, OccurredAt=occurredAt,
            PayloadJson=JsonSerializer.Serialize(payload), CreatedAt=DateTimeOffset.UtcNow
        };
        db.Events.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return ToEnvelope(row);
    }

    public async Task<IReadOnlyList<CafeteriaServiceEventEnvelope>> GetAfterAsync(long afterSequence, int take, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Events.AsNoTracking().Where(x => x.Sequence > afterSequence).OrderBy(x => x.Sequence).Take(Math.Clamp(take,1,500)).ToListAsync(cancellationToken);
        return rows.Select(ToEnvelope).ToArray();
    }

    public async Task<IReadOnlyList<AdditionalCreditApproval>> GetAdditionalCreditApprovalsAsync(int customerId, int year, int month, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var candidates = await db.Events.AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.Kind == (int)CafeteriaServiceEventKind.AdditionalCredit)
            .ToListAsync(cancellationToken);
        // DateTimeOffset date-part translation is deliberately done in memory for SQLite compatibility.
        var rows = candidates.Where(x => x.OccurredAt.Year == year && x.OccurredAt.Month == month)
            .OrderBy(x => x.Sequence)
            .ToList();
        return rows.Select(x => JsonSerializer.Deserialize<AdditionalCreditServiceEvent>(x.PayloadJson))
            .Where(x => x is not null)
            .Select(x => new AdditionalCreditApproval(x!.OperationId, x.CustomerId, x.Amount, x.ApprovedBy, x.ApprovedAt, x.Reason))
            .ToArray();
    }

    public async Task<decimal> GetAdditionalCreditTotalAsync(int customerId, int year, int month, CancellationToken cancellationToken = default)
        => (await GetAdditionalCreditApprovalsAsync(customerId, year, month, cancellationToken)).Sum(x => x.Amount);


    public async Task<CafeteriaServiceEventEnvelope?> GetByOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Events.AsNoTracking().SingleOrDefaultAsync(x => x.OperationId == operationId, cancellationToken);
        return row is null ? null : ToEnvelope(row);
    }

    public async Task<bool> HasOperationAsync(Guid operationId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Events.AnyAsync(x => x.OperationId == operationId, cancellationToken);
    }

    public async Task<IReadOnlyList<CafeteriaServiceEventEnvelope>> GetPendingFfpPublicationAsync(int take, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var rows = await db.Events.AsNoTracking()
            .Where(x => x.FfpPublishedAt == null)
            .OrderBy(x => x.Sequence)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
        return rows.Select(ToEnvelope).ToArray();
    }

    public async Task RecordFfpAttemptAsync(Guid operationId, string? error, bool succeeded, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Events.SingleAsync(x => x.OperationId == operationId, cancellationToken);
        row.FfpAttemptCount++;
        row.FfpLastAttemptAt = DateTimeOffset.UtcNow;
        row.FfpLastError = succeeded ? null : error;
        if (succeeded) row.FfpPublishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static CafeteriaServiceEventEnvelope ToEnvelope(ServiceEventRow x)
        => new(x.Sequence, x.OperationId, (CafeteriaServiceEventKind)x.Kind, x.CustomerId, x.OccurredAt, x.PayloadJson);
}
