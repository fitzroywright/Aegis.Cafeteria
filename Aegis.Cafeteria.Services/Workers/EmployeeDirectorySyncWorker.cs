namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Identity;

public sealed class EmployeeDirectorySyncWorker
(
      IWorkforceReferenceSource workforceSource
    , EmployeeMetadataStore metadataStore
    , IEnumerable<IEmployeeMetadataProvider> providers
    , ILogger<EmployeeDirectorySyncWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        IEmployeeMetadataProvider? provider = providers.SingleOrDefault();
        if (provider is null)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(provider.SourceId);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (workforceSource is IPagedWorkforceReferenceSource paged)
                {
                    int? cursor = null;
                    do
                    {
                        CustomerReferenceBatch batch = await paged.GetCustomerBatchAsync(cursor, 10, stoppingToken);
                        await RefreshAsync(batch.Customers, provider, stoppingToken);
                        if (batch.NextCustomerId is int next && (batch.Customers.Count == 0 || next <= (cursor ?? int.MinValue)))
                        {
                            throw new InvalidOperationException("Employee metadata refresh cursor did not advance.");
                        }

                        cursor = batch.NextCustomerId;
                    }
                    while (cursor.HasValue);
                }
                else
                {
                    IReadOnlyList<ExternalCustomerSnapshot> employees = await workforceSource.GetCustomersAsync(stoppingToken);
                    foreach (ExternalCustomerSnapshot[] batch in employees.Chunk(10))
                    {
                        await RefreshAsync(batch, provider, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Employee metadata sync failed; last-known fields remain available");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task RefreshAsync(IReadOnlyList<ExternalCustomerSnapshot> employees, IEmployeeMetadataProvider provider, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<int, EmployeeMetadataPatch> updates = await provider.GetAsync(employees, cancellationToken);
        HashSet<int> requested = employees.Select(employee => employee.CustomerId).ToHashSet();
        if (updates.Keys.Any(id => !requested.Contains(id)))
        {
            throw new InvalidOperationException("Employee metadata provider returned an unrequested employee.");
        }

        await metadataStore.ApplyAsync(updates, cancellationToken);
    }
}
