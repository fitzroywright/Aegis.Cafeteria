namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;
using System.Net.Http.Json;

/// <summary>POS-side adapter. Cafeteria terminals never connect directly to FFP SQL.</summary>
public sealed class ServicesIntegrationGateway(HttpClient http) :
      IExternalAccountingGateway
    , IWorkforceReferenceSource
    , ILunchItemBootstrapSource
    , IFfpMonthImportSource
{
    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        ExternalCustomerSnapshot[] customers = await GetCustomersOnceAsync(cancellationToken);

        // The Services-side FFP adapter warms its photo cache independently from the main employee query.
        // On a cold service start the first response can therefore contain employee identity data before
        // the photo cache has finished loading. Briefly re-query so a manual/startup refresh persists the
        // photos as part of the same user-visible refresh operation instead of requiring a second attempt.
        if (customers.Length > 0 && customers.All(customer => customer.Photo is null || customer.Photo.Length == 0))
        {
            for (int attempt = 0; attempt < 4; attempt++)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
                ExternalCustomerSnapshot[] retry = await GetCustomersOnceAsync(cancellationToken);
                customers = retry;
                if (customers.Any(customer => customer.Photo is { Length: > 0 }))
                {
                    break;
                }
            }
        }

        return customers;
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<DateOnly[]>($"api/ffp/holidays?year={year}&month={month}", cancellationToken) ?? [];

    public async Task<IReadOnlyList<ExternalProductSnapshot>> GetLunchItemsAsync(CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<ExternalProductSnapshot[]>("api/ffp/lunch-items", cancellationToken) ?? [];

    public async Task<FfpMonthImportSnapshot> GetMonthAsync(int year, int month, CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<FfpMonthImportSnapshot>($"api/ffp/month-import?year={year}&month={month}", cancellationToken)
            ?? throw new InvalidOperationException("Cafeteria Services returned no current-month import payload.");

    public async Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync(CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<ExternalAccountSummary[]>("api/ffp/accounts", cancellationToken) ?? [];

    public async Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("api/ffp/ledger", entry, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<ExternalCustomerSnapshot[]> GetCustomersOnceAsync(CancellationToken cancellationToken)
        => await http.GetFromJsonAsync<ExternalCustomerSnapshot[]>("api/ffp/customers", cancellationToken) ?? [];
}
