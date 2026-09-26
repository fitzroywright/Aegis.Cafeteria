namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed record PosSynchronizationSnapshot(
    int Pending,
    int Failed,
    DateTimeOffset? OldestPendingAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    DateTimeOffset? LastSuccessfulAtUtc,
    bool LocalStoreHealthy,
    DateTimeOffset ObservedAtUtc);

public sealed class LocalPersistenceStore(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    ILogger<LocalPersistenceStore> logger)
{
    private readonly FinancialPostingService financialPostingService = new();

    public Task SaveCustomerAsync(CustomerRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.Customers.SingleOrDefaultAsync(v=>v.Id==row.Id,ct); if(x is null) db.Customers.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveCustomerIdentifierAsync(CustomerIdentifierRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.CustomerIdentifiers.SingleOrDefaultAsync(v=>v.Id==row.Id,ct); if(x is null) db.CustomerIdentifiers.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveProductAsync(ProductRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.Products.SingleOrDefaultAsync(v=>v.Id==row.Id,ct); if(x is null) db.Products.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveOrderAsync(OrderRow row, CancellationToken ct = default)
        => SaveAsync(db => financialPostingService.SaveOrderAsync(db, row, ct), ct);
    public Task SaveOrderWithTransactionAsync(OrderRow order, PosTransactionOutboxRow transaction, CancellationToken ct = default)
        => SaveAsync(async db =>
        {
            await financialPostingService.SaveOrderAsync(db, order, ct);
            bool exists = await db.PosTransactionOutbox.AnyAsync(x => x.EventId == transaction.EventId, ct);
            if (!exists) db.PosTransactionOutbox.Add(transaction);
        }, ct);
    public Task SaveAccountEventAsync(AccountEventRow row, CancellationToken ct = default)
        => SaveAsync(db => financialPostingService.SaveAccountEventAsync(db, row, ct), ct);
    public Task SaveAccountPeriodAsync(AccountPeriodRow row, CancellationToken ct = default)
        => SaveAsync(async db => { var x=await db.AccountPeriods.SingleOrDefaultAsync(v=>v.CustomerId==row.CustomerId&&v.Year==row.Year&&v.Month==row.Month,ct); if(x is null) db.AccountPeriods.Add(row); else db.Entry(x).CurrentValues.SetValues(row); }, ct);
    public Task SaveMonthlyCreditAllocationAsync(MonthlyCreditAllocationRow row, CancellationToken ct = default)
        => SaveAsync(db => financialPostingService.SaveMonthlyCreditAllocationAsync(db, row, ct), ct);
    public async Task<MonthlyCreditAllocationRow?> GetMonthlyCreditAllocationAsync(int customerId,int year,int month,CancellationToken ct=default)
    { await using var db=await factory.CreateDbContextAsync(ct); return await db.MonthlyCreditAllocations.AsNoTracking().SingleOrDefaultAsync(x=>x.CustomerId==customerId&&x.Year==year&&x.Month==month,ct); }

    public Task EnqueuePosTransactionAsync(PosTransactionOutboxRow row, CancellationToken ct = default)
        => SaveAsync(async db => { bool exists = await db.PosTransactionOutbox.AnyAsync(x => x.EventId == row.EventId, ct); if (!exists) db.PosTransactionOutbox.Add(row); }, ct);

    public async Task<IReadOnlyList<PosTransactionOutboxRow>> GetPendingPosTransactionsAsync(int take, DateTimeOffset now, CancellationToken ct = default)
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(ct);

        int requested = Math.Clamp(take, 1, 100);

        // SQLite/EF cannot reliably translate DateTimeOffset comparisons for
        // NextAttemptAt. Keep the SQL predicate to the nullable publication
        // state, then apply retry-time ordering/filtering in memory over a
        // bounded candidate set.
        PosTransactionOutboxRow[] candidates = await db.PosTransactionOutbox
            .AsNoTracking()
            .Where(x => x.PublishedAt == null)
            .Take(Math.Max(500, requested * 5))
            .ToArrayAsync(ct);

        return candidates
            .Where(x => x.NextAttemptAt <= now)
            .OrderBy(x => x.CreatedAt)
            .Take(requested)
            .ToArray();
    }

    public Task MarkPosTransactionPublishedAsync(Guid eventId, DateTimeOffset publishedAt, CancellationToken ct = default)
        => SaveAsync(async db =>
        {
            PosTransactionOutboxRow row = await db.PosTransactionOutbox.SingleAsync(x => x.EventId == eventId, ct);
            row.AttemptCount++;
            row.LastAttemptAt = publishedAt;
            row.PublishedAt = publishedAt;
            row.LastError = null;
        }, ct);

    public Task RecordPosTransactionFailureAsync(Guid eventId, string error, DateTimeOffset attemptedAt, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
        => SaveAsync(async db =>
        {
            PosTransactionOutboxRow row = await db.PosTransactionOutbox.SingleAsync(x => x.EventId == eventId, ct);
            row.AttemptCount++;
            row.LastAttemptAt = attemptedAt;
            row.NextAttemptAt = nextAttemptAt;
            row.LastError = string.IsNullOrWhiteSpace(error) ? "Unknown publication error." : error[..Math.Min(error.Length, 2000)];
        }, ct);

    public async Task<PosSynchronizationSnapshot> GetPosSynchronizationSnapshotAsync(CancellationToken ct = default)
    {
        try
        {
            await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(ct);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int pending = await db.PosTransactionOutbox.CountAsync(x => x.PublishedAt == null, ct);
            int failed = await db.PosTransactionOutbox.CountAsync(x => x.PublishedAt == null && x.LastError != null, ct);
            DateTimeOffset? oldestPending = await db.PosTransactionOutbox
                .Where(x => x.PublishedAt == null)
                .OrderBy(x => x.CreatedAt)
                .Select(x => (DateTimeOffset?)x.CreatedAt)
                .FirstOrDefaultAsync(ct);
            DateTimeOffset? lastAttempt = await db.PosTransactionOutbox
                .Where(x => x.LastAttemptAt != null)
                .OrderByDescending(x => x.LastAttemptAt)
                .Select(x => x.LastAttemptAt)
                .FirstOrDefaultAsync(ct);
            DateTimeOffset? lastSuccess = await db.PosTransactionOutbox
                .Where(x => x.PublishedAt != null)
                .OrderByDescending(x => x.PublishedAt)
                .Select(x => x.PublishedAt)
                .FirstOrDefaultAsync(ct);

            return new(pending, failed, oldestPending, lastAttempt, lastSuccess, true, now);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(0, 0, null, null, null, false, DateTimeOffset.UtcNow);
        }
    }

    private async Task SaveAsync(Func<LocalCafeteriaDbContext,Task> action,CancellationToken ct)
    { await using var db=await factory.CreateDbContextAsync(ct); await using var tx=await db.Database.BeginTransactionAsync(ct); await action(db); await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); logger.LogDebug("Local cafeteria state committed"); }
}
