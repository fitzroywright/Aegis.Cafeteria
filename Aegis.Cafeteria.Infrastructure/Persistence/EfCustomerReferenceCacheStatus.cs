namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public sealed class EfCustomerReferenceCacheStatus(IDbContextFactory<LocalCafeteriaDbContext> factory) : ICustomerReferenceCacheStatus
{
    private readonly object gate = new();
    private DateTimeOffset? lastAttemptUtc;
    private DateTimeOffset? lastSuccessUtc;
    private string? lastError;
    private bool refreshInProgress;
    private int refreshedEmployeeCount;
    private int completedBatchCount;

    public async Task<CustomerReferenceCacheStatus> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var identifiers = await db.CustomerIdentifiers.AsNoTracking().ToListAsync(cancellationToken);
        int employeeCount = await db.Customers.CountAsync(cancellationToken);
        var identifierCount = identifiers.Count;
        var cacheUpdatedAtUtc = identifiers.Count == 0
            ? (DateTimeOffset?)null
            : identifiers.Max(x => x.UpdatedAt);

        lock (gate)
        {
            return new CustomerReferenceCacheStatus(
                employeeCount,
                identifierCount,
                cacheUpdatedAtUtc,
                lastAttemptUtc,
                lastSuccessUtc,
                lastError,
                refreshInProgress)
            {
                  RefreshedEmployeeCount = refreshedEmployeeCount
                , CompletedBatchCount = completedBatchCount
            };
        }
    }

    public void RefreshStarted()
    {
        lock (gate)
        {
            lastAttemptUtc = DateTimeOffset.UtcNow;
            refreshInProgress = true;
            refreshedEmployeeCount = 0;
            completedBatchCount = 0;
            lastError = null;
        }
    }

    public void RefreshProgress(int employeeCount, int batchCount)
    {
        lock (gate)
        {
            refreshedEmployeeCount = employeeCount;
            completedBatchCount = batchCount;
        }
    }

    public void RefreshSucceeded(int employeeCount)
    {
        lock (gate)
        {
            lastSuccessUtc = DateTimeOffset.UtcNow;
            lastAttemptUtc = lastSuccessUtc;
            lastError = null;
            refreshInProgress = false;
        }
    }

    public void RefreshFailed(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        lock (gate)
        {
            lastAttemptUtc = DateTimeOffset.UtcNow;
            lastError = exception.GetBaseException().Message;
            refreshInProgress = false;
        }
    }
}
