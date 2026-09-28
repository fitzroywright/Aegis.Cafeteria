namespace Aegis.Cafeteria.Application.Integration;

using Microsoft.Extensions.Logging;

public sealed class ReferenceDataRefreshService(
    IWorkforceReferenceSource source,
    IWorkforceReferenceCache cache,
    ICustomerReferenceCacheStatus cacheStatus,
    ILogger<ReferenceDataRefreshService> logger)
{
    private readonly SemaphoreSlim customerRefreshGate = new(1, 1);
    private readonly object backgroundGate = new();
    private Task? backgroundRefresh;
    private ReferenceRefreshRunStatus backgroundStatus = new(false, "Ready to refresh");

    public ReferenceRefreshRunStatus BackgroundStatus
    {
        get { lock (backgroundGate) { return backgroundStatus; } }
    }

    public Task WaitForBackgroundRefreshAsync()
    {
        lock (backgroundGate) { return backgroundRefresh ?? Task.CompletedTask; }
    }

    public bool StartBackgroundRefresh(DateOnly today)
    {
        lock (backgroundGate)
        {
            if (backgroundRefresh is { IsCompleted: false }) return false;
            backgroundStatus = new(true, "Refreshing employees...");
            backgroundRefresh = Task.Run(async () =>
            {
                try
                {
                    int count = await RefreshCustomersAsync();
                    lock (backgroundGate) { backgroundStatus = new(true, $"{count:N0} employees saved • refreshing holidays..."); }
                    await RefreshHolidaysAsync(today);
                    lock (backgroundGate) { backgroundStatus = new(false, $"Refresh complete • {count:N0} employees saved"); }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Background reference refresh failed; committed batches remain available");
                    lock (backgroundGate) { backgroundStatus = new(false, $"Refresh failed: {exception.GetBaseException().Message}"); }
                }
            });
            return true;
        }
    }

    public async Task<int> RefreshCustomersAsync(CancellationToken cancellationToken = default)
    {
        if (!await customerRefreshGate.WaitAsync(0, cancellationToken))
        {
            throw new InvalidOperationException("An employee refresh is already in progress.");
        }
        cacheStatus.RefreshStarted();
        try
        {
            if (source is IPagedWorkforceReferenceSource pagedSource && cache is IBatchedWorkforceReferenceCache batchedCache)
            {
                HashSet<int> refreshedIds = [];
                int? cursor = null;
                int batchCount = 0;
                do
                {
                    CustomerReferenceBatch batch = await pagedSource.GetCustomerBatchAsync(cursor, pagedSource.CustomerBatchSize, cancellationToken);
                    int? previousId = cursor;
                    foreach (ExternalCustomerSnapshot customer in batch.Customers)
                    {
                        if (previousId.HasValue && customer.CustomerId <= previousId.Value)
                        {
                            throw new InvalidOperationException("Services returned an unordered employee batch.");
                        }
                        previousId = customer.CustomerId;
                    }
                    if (batch.NextCustomerId.HasValue && (batch.Customers.Count == 0 || batch.NextCustomerId != previousId))
                    {
                        throw new InvalidOperationException("Services returned an invalid employee continuation cursor.");
                    }
                    ExternalCustomerSnapshot[] eligibleCustomers = batch.Customers
                        .Where(customer => customer.IsActive && customer.IsEligibleForCafeteriaCredit).ToArray();
                    if (eligibleCustomers.Length > 0)
                    {
                        await batchedCache.UpsertCustomerBatchAsync(eligibleCustomers, refreshedIds, cancellationToken);
                        refreshedIds.UnionWith(eligibleCustomers.Select(customer => customer.CustomerId));
                        cacheStatus.RefreshProgress(refreshedIds.Count, ++batchCount);
                    }
                    cursor = batch.NextCustomerId;
                } while (cursor.HasValue);
                await batchedCache.CompleteCustomerRefreshAsync(refreshedIds, cancellationToken);
                cacheStatus.RefreshSucceeded(refreshedIds.Count);
                logger.LogInformation("Employee refresh complete: {Count} employees in {Batches} batches", refreshedIds.Count, batchCount);
                return refreshedIds.Count;
            }
            IReadOnlyList<ExternalCustomerSnapshot> sourceCustomers = await source.GetCustomersAsync(cancellationToken);
            ExternalCustomerSnapshot[] customers = sourceCustomers
                .Where(customer => customer.IsActive && customer.IsEligibleForCafeteriaCredit).ToArray();
            await cache.ReplaceCustomersAsync(customers, cancellationToken);
            cacheStatus.RefreshSucceeded(customers.Length);
            int photoCount = customers.Count(customer => customer.Photo is { Length: > 0 });
            logger.LogInformation(
                "Workforce customer refresh complete: {CustomerCount} customers; {PhotoCount} photos",
                customers.Length,
                photoCount);
            return customers.Length;
        }
        catch (Exception ex)
        {
            cacheStatus.RefreshFailed(ex);
            throw;
        }
        finally
        {
            customerRefreshGate.Release();
        }
    }

    public async Task<int> RefreshHolidaysAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DateOnly> holidays = await source.GetHolidaysAsync(today.Year, today.Month, cancellationToken);
        await cache.ReplaceHolidaysAsync(today.Year, today.Month, holidays, cancellationToken);
        logger.LogInformation("Workforce holiday refresh complete: {HolidayCount} holidays for {Year}-{Month:D2}", holidays.Count, today.Year, today.Month);
        return holidays.Count;
    }

    public async Task RefreshAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        await RefreshCustomersAsync(cancellationToken);
        await RefreshHolidaysAsync(today, cancellationToken);
    }
}

public sealed record ReferenceRefreshRunStatus(bool Running, string Message);

