namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class CafeteriaPolicyStartupWorker(
    ICafeteriaPolicyProvider policyProvider,
    ILogger<CafeteriaPolicyStartupWorker> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        DateTime now = DateTime.Now;
        CafeteriaPolicySnapshot policy = await policyProvider.GetPolicyAsync(
            now.Year,
            now.Month,
            forceReload: true,
            cancellationToken);

        logger.LogInformation(
            "Loaded authoritative Cafeteria policy {Version} for {Year}-{Month:D2} with {WorkingDayCount} working days",
            policy.Version,
            policy.Year,
            policy.Month,
            policy.WorkingDays.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken)
        => Task.CompletedTask;
}
