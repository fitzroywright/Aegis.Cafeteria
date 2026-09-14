namespace Aegis.Cafeteria.Infrastructure.Messaging;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class WeeklySummaryWorker(
    CafeteriaMessagingOptions options,
    WeeklyCustomerSummaryService service,
    ILogger<WeeklySummaryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                var today = DateOnly.FromDateTime(now.Date);
                var daysSinceMonday = ((int)now.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
                var currentWeekStart = today.AddDays(-daysSinceMonday);
                var previousWeekStart = currentWeekStart.AddDays(-7);
                var previousWeekEnd = previousWeekStart.AddDays(6);

                // Do not send the just-completed week before the configured Monday delivery hour.
                // After that point we keep retrying on later days until the durable dispatch rows show success.
                var deliveryOffset = ((int)options.WeeklySummaryDay - (int)DayOfWeek.Monday + 7) % 7;
                var allowed = daysSinceMonday > deliveryOffset ||
                    (daysSinceMonday == deliveryOffset && now.Hour >= options.WeeklySummaryHourLocal);
                if (allowed)
                    await service.SendWeekAsync(previousWeekStart, previousWeekEnd, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Weekly cafeteria customer summary dispatch failed; it will be retried"); }
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }
}
