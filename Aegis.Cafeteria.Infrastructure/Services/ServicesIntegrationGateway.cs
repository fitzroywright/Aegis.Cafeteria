namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;
using System.Net.Http.Json;

/// <summary>POS-side adapter. Cafeteria terminals communicate only with Cafeteria Services and never with an external system database directly.</summary>
public sealed class ServicesIntegrationGateway(HttpClient http, Aegis.Cafeteria.Infrastructure.Configuration.CafeteriaServicesOptions? options = null) :
      IExternalAccountingGateway
    , IWorkforceReferenceSource
    , IPagedWorkforceReferenceSource
{
    public int CustomerBatchSize => Math.Clamp(options?.EmployeeRefreshBatchSize ?? 10, 1, 100);

    public async Task<CustomerReferenceBatch> GetCustomerBatchAsync(int? afterCustomerId, int take, CancellationToken cancellationToken = default)
    {
        string path = $"api/reference/customers/batch?take={take}";
        if (afterCustomerId.HasValue)
        {
            path += $"&afterCustomerId={afterCustomerId.Value}";
        }
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await http.GetFromJsonAsync<CustomerReferenceBatch>(path, cancellationToken)
                    ?? throw new InvalidOperationException("Services returned an empty employee batch response.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < 2)
            {
                // Retry only this read when HttpClient's per-request timeout expires.
            }
            catch (HttpRequestException exception) when (attempt < 2
                && (exception.StatusCode is null || (int)exception.StatusCode.Value >= 500))
            {
                // Authentication and other client errors must remain visible immediately.
            }
            await Task.Delay(TimeSpan.FromMilliseconds(500 * (attempt + 1)), cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        List<ExternalCustomerSnapshot> customers = [];
        int? cursor = null;
        do
        {
            CustomerReferenceBatch batch = await GetCustomerBatchAsync(cursor, CustomerBatchSize, cancellationToken);
            customers.AddRange(batch.Customers);
            cursor = batch.NextCustomerId;
        } while (cursor.HasValue);
        return customers;
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<DateOnly[]>($"api/reference/holidays?year={year}&month={month}", cancellationToken) ?? [];


    public async Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("api/integration/ledger", entry, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

}
