namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

public sealed class MonthOpeningWorker(
    IWorkforceReferenceSource workforce,
    IProductCatalogSource catalogSource,
    MonthlyAllocationCalculator calculator,
    ServiceEventStore events,
    MonthOpeningOptions options,
    ILogger<MonthOpeningWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = DateTime.Now;
                if (now.Hour >= options.RunHourLocal)
                    await EnsureMonthAsync(now.Year, now.Month, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Cafeteria month-opening allocation job failed; it will retry"); }
            await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
        }
    }

    internal async Task<int> EnsureMonthAsync(int year, int month, CancellationToken cancellationToken)
    {
        var employees = await workforce.GetCustomersAsync(cancellationToken);
        var holidays = await workforce.GetHolidaysAsync(year, month, cancellationToken);
        var catalog = await catalogSource.GetCatalogAsync(cancellationToken);
        var workingDays = calculator.CountWorkingDays(year, month, holidays);
        var created = 0;
        foreach (var employee in employees.Where(x => x.IsEligibleForCafeteriaCredit))
        {
            var operationId = DeterministicOperationId(employee.CustomerId, year, month);
            if (await events.HasOperationAsync(operationId, cancellationToken)) continue;
            var allocation = calculator.Calculate(employee.CustomerId, year, month, workingDays, true, catalog.CreditLimitPrice, catalog.SubsidyCost, DateTimeOffset.UtcNow);
            var payload = new MonthlyAllocationServiceEvent(operationId, employee.CustomerId, year, month, workingDays, true, allocation.CreditLimitPrice, allocation.SubsidyCost, allocation.CalculatedAt, employee.Name);
            await events.AppendAsync(operationId, CafeteriaServiceEventKind.MonthlyAllocation, employee.CustomerId, allocation.CalculatedAt, payload, cancellationToken);
            created++;
        }
        if (created > 0) logger.LogInformation("Created {Count} Cafeteria month-opening allocation events for {Year}-{Month:D2}", created, year, month);
        return created;
    }

    private static Guid DeterministicOperationId(int customerId, int year, int month)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{customerId}:{year:D4}-{month:D2}:monthly-allocation"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
