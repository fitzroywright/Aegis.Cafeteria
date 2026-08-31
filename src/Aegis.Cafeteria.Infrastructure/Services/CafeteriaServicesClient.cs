namespace Aegis.Cafeteria.Infrastructure.Services;

using Aegis.Cafeteria.Domain.Services;
using System.Net.Http.Json;

public sealed class CafeteriaServicesClient(HttpClient http)
{
    public async Task<IReadOnlyList<CafeteriaServiceEventEnvelope>> GetEventsAsync(long afterSequence, int take, CancellationToken cancellationToken = default)
    {
        var path = $"api/pos/events?afterSequence={afterSequence}&take={Math.Clamp(take, 1, 500)}";
        return await http.GetFromJsonAsync<CafeteriaServiceEventEnvelope[]>(path, cancellationToken) ?? [];
    }
}
