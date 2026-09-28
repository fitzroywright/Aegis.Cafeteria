namespace Aegis.Cafeteria.Application.Integration;

using Microsoft.Extensions.Logging;

public sealed class ReferenceDataRefreshService(
    IWorkforceReferenceSource source,
    IWorkforceReferenceCache cache,
    ICustomerReferenceCacheStatus cacheStatus,
    ILogger<ReferenceDataRefreshService> logger)
{
    private readonly SemaphoreSlim customerRefreshGate = new(1, 1);

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
                    if (batch.Customers.Count > 0)
                    {
                        await batchedCache.UpsertCustomerBatchAsync(batch.Customers.ToArray(), refreshedIds, cancellationToken);
                        refreshedIds.UnionWith(batch.Customers.Select(customer => customer.CustomerId));
                        cacheStatus.RefreshProgress(refreshedIds.Count, ++batchCount);
                    }
                    cursor = batch.NextCustomerId;
                } while (cursor.HasValue);
                await batchedCache.CompleteCustomerRefreshAsync(refreshedIds, cancellationToken);
                cacheStatus.RefreshSucceeded(refreshedIds.Count);
                logger.LogInformation("Employee refresh complete: {Count} employees in {Batches} batches", refreshedIds.Count, batchCount);
                return refreshedIds.Count;
            }
            IReadOnlyList<ExternalCustomerSnapshot> customers = await source.GetCustomersAsync(cancellationToken);
            await cache.ReplaceCustomersAsync(customers, cancellationToken);
            cacheStatus.RefreshSucceeded(customers.Count);
            int photoCount = customers.Count(customer => customer.Photo is { Length: > 0 });
            logger.LogInformation(
                "Workforce customer refresh complete: {CustomerCount} customers; {PhotoCount} photos",
                customers.Count,
                photoCount);
            return customers.Count;
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
