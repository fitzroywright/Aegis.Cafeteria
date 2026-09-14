namespace Aegis.Cafeteria.Application.Integration;

using Microsoft.Extensions.Logging;

public sealed class ReferenceDataRefreshService(
    IWorkforceReferenceSource source,
    IWorkforceReferenceCache cache,
    ICustomerReferenceCacheStatus cacheStatus,
    ILogger<ReferenceDataRefreshService> logger)
{
    public async Task<int> RefreshCustomersAsync(CancellationToken cancellationToken = default)
    {
        cacheStatus.RefreshStarted();
        try
        {
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
