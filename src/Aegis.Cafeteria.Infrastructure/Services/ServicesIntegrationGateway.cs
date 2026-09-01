namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Integration;
using System.Net.Http.Json;

/// <summary>POS-side adapter. When Cafeteria Services is enabled the terminal never connects directly to FFP SQL.</summary>
public sealed class ServicesIntegrationGateway(HttpClient http) : IExternalAccountingGateway, IWorkforceReferenceSource, ILunchItemBootstrapSource
{
    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<ExternalCustomerSnapshot[]>("api/ffp/customers", cancellationToken) ?? [];

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<DateOnly[]>($"api/ffp/holidays?year={year}&month={month}", cancellationToken) ?? [];

    public async Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync(CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<ExternalAccountSummary[]>("api/ffp/accounts", cancellationToken) ?? [];

    public async Task<IReadOnlyList<ExternalProductSnapshot>> GetLunchItemsAsync(CancellationToken cancellationToken = default)
        => await http.GetFromJsonAsync<ExternalProductSnapshot[]>("api/ffp/lunch-items", cancellationToken) ?? [];

    public async Task PublishLedgerEntryAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("api/ffp/ledger", entry, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
