namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

/// <summary>
/// Current hosted adapter worker. The worker name remains Ffp for operational clarity, but the
/// application service it drives is source-neutral.
/// </summary>
public sealed class FfpReferenceRefreshWorker(
    ReferenceDataRefreshService refresh,
    ProductCatalogRefreshService products,
    FfpIntegrationOptions options,
    ILogger<FfpReferenceRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            await TryRefreshAsync("customers", () => refresh.RefreshCustomersAsync(stoppingToken), stoppingToken);
            await TryRefreshAsync("holidays", () => refresh.RefreshHolidaysAsync(today, stoppingToken), stoppingToken);
            await TryRefreshAsync("LunchItems catalog and rates", () => products.RefreshAsync(stoppingToken), stoppingToken);

            try { await Task.Delay(TimeSpan.FromMinutes(options.RefreshIntervalMinutes), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }

    private async Task TryRefreshAsync(string component, Func<Task<int>> action, CancellationToken stoppingToken)
    {
        try { await action(); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "FFP adapter {Component} refresh failed; POS continues with its last durable local cache", component); }
    }
}
