namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class ReferenceRefreshWorker(
    ReferenceDataRefreshService refresh,
    CafeteriaServicesOptions options,
    ICustomerReferenceCacheStatus cacheStatus,
    ILogger<ReferenceRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan refreshInterval = TimeSpan.FromMinutes(Math.Max(1, options.ReferenceRefreshIntervalMinutes));
            CustomerReferenceCacheStatus status = await cacheStatus.GetAsync(stoppingToken);
            DateTimeOffset? freshness = status.LastSuccessUtc ?? status.CacheUpdatedAtUtc;

            if (freshness.HasValue)
            {
                TimeSpan age = DateTimeOffset.UtcNow - freshness.Value;
                if (age < refreshInterval)
                {
                    try
                    {
                        await Task.Delay(refreshInterval - age, stoppingToken);
                        continue;
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }

            DateOnly today = DateOnly.FromDateTime(DateTime.Today);

            await TryRefreshAsync(
                "customers",
                () => refresh.RefreshCustomersAsync(stoppingToken),
                stoppingToken);

            await TryRefreshAsync(
                "holidays",
                () => refresh.RefreshHolidaysAsync(today, stoppingToken),
                stoppingToken);

            try
            {
                await Task.Delay(refreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task TryRefreshAsync(
        string component,
        Func<Task<int>> action,
        CancellationToken stoppingToken)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Reference {Component} refresh failed; POS continues with its last durable local cache",
                component);
        }
    }
}
