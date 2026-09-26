namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Identity;
using Common.Security.Abstractions;
using Common.Security.Models;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class EmployeeDirectorySyncWorker(
    IWorkforceReferenceSource workforceSource,
    DirectoryProfileCache directoryCache,
    IDirectoryProfileProvider directory,
    ILogger<EmployeeDirectorySyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                IReadOnlyList<ExternalCustomerSnapshot> employees = await workforceSource.GetCustomersAsync(stoppingToken);
                string[] loginNames = employees
                    .Select(employee => employee.LoginName)
                    .Where(loginName => !string.IsNullOrWhiteSpace(loginName))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                IReadOnlyDictionary<string, DirectoryUserProfile> records =
                    await directory.GetByLoginNamesAsync(loginNames, stoppingToken);
                directoryCache.Replace(records);

                logger.LogInformation(
                    "Background AD employee metadata sync refreshed {DirectoryCount} of {EmployeeCount} eligible employees",
                    records.Count,
                    employees.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "Background AD employee metadata sync failed; existing cached metadata will remain in use");
            }

            try
            {
                await Task.Delay(RefreshInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
