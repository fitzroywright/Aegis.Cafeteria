using System.Text.Json;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;

namespace Aegis.Cafeteria.Infrastructure.Health;

public sealed class CafeteriaHeartbeatWorker(
      CafeteriaHealthOptions options
    , CafeteriaPersistenceOptions persistence
    , ICustomerReferenceCacheStatus employeeCacheStatus
    , ILogger<CafeteriaHeartbeatWorker> logger) : BackgroundService
{
    private DateTimeOffset lastGraylogHeartbeat = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;

            try
            {
                CustomerReferenceCacheStatus cache = await employeeCacheStatus.GetAsync(stoppingToken);
                int? age = cache.CacheUpdatedAtUtc.HasValue
                    ? Math.Max(0, (int)(now - cache.CacheUpdatedAtUtc.Value).TotalMinutes)
                    : null;

                object payload = new
                {
                      status = cache.EmployeeCount == 0 || age >= options.EmployeeCacheCriticalMinutes ? "degraded" : "running"
                    , application = "Aegis.Cafeteria.POS"
                    , terminalId = persistence.TerminalId
                    , machine = Environment.MachineName
                    , processId = Environment.ProcessId
                    , timeUtc = now
                    , employeeCacheCount = cache.EmployeeCount
                    , employeeCacheIdentifierCount = cache.IdentifierCount
                    , employeeCacheLastRefreshUtc = cache.CacheUpdatedAtUtc
                    , employeeCacheAgeMinutes = age
                    , employeeCacheLastError = cache.LastError
                    , employeeCacheHealthy = cache.EmployeeCount > 0 && age < options.EmployeeCacheCriticalMinutes
                };

                string full = Path.GetFullPath(options.HeartbeatPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);

                string temp = full + ".tmp";
                await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(payload), stoppingToken);
                File.Move(temp, full, true);

                if (now - lastGraylogHeartbeat >= TimeSpan.FromMinutes(options.GraylogHeartbeatEveryMinutes))
                {
                    logger.LogInformation(
                        "Cafeteria heartbeat terminal={TerminalId} employeeCacheCount={EmployeeCacheCount} employeeCacheAgeMinutes={EmployeeCacheAgeMinutes}"
                        , persistence.TerminalId
                        , cache.EmployeeCount
                        , age);

                    lastGraylogHeartbeat = now;
                }

                await Task.Delay(TimeSpan.FromSeconds(options.IntervalSeconds), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to write cafeteria heartbeat");
            }
        }
    }
}
