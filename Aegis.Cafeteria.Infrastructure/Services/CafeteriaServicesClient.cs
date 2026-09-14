namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Services;
using System.Net.Http.Json;

public sealed record CustomerNotificationPreferenceSnapshot
(
      int CustomerId
    , bool PreferenceAsked
    , bool WeeklySummaryEnabled
    , string? EmailAddress
);

public sealed class CafeteriaServicesClient(HttpClient http)
{
    public async Task<IReadOnlyList<CafeteriaServiceEventEnvelope>> GetEventsAsync
    (
          long afterSequence
        , int take
        , CancellationToken cancellationToken = default
    )
    {
        string path = $"api/pos/events?afterSequence={afterSequence}&take={Math.Clamp(take, 1, 500)}";
        return await http.GetFromJsonAsync<CafeteriaServiceEventEnvelope[]>(path, cancellationToken) ?? [];
    }

    public async Task PublishTransactionEventAsync(PosTransactionEvent transactionEvent, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync("api/pos/transactions", transactionEvent, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<CustomerNotificationPreferenceSnapshot> GetNotificationPreferenceAsync
    (
          int customerId
        , CancellationToken cancellationToken = default
    )
    {
        return await http.GetFromJsonAsync<CustomerNotificationPreferenceSnapshot>
        (
              $"api/pos/customers/{customerId}/notification-preference"
            , cancellationToken
        ) ?? throw new InvalidOperationException("Cafeteria Services returned no notification preference.");
    }

    public async Task<CustomerNotificationPreferenceSnapshot> SetNotificationPreferenceAsync
    (
          int customerId
        , bool weeklySummaryEnabled
        , string? emailAddress
        , CancellationToken cancellationToken = default
    )
    {
        using HttpResponseMessage response = await http.PutAsJsonAsync
        (
              $"api/pos/customers/{customerId}/notification-preference"
            , new
            {
                  WeeklySummaryEnabled = weeklySummaryEnabled
                , EmailAddress = emailAddress
            }
            , cancellationToken
        );

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<CustomerNotificationPreferenceSnapshot>
        (
            cancellationToken: cancellationToken
        ) ?? throw new InvalidOperationException("Cafeteria Services returned no notification preference.");
    }
}
