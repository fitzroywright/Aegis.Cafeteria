using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Realtime;

namespace Aegis.Cafeteria.Services;

internal static class PosEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapHub<ManagementEventsHub>("/hubs/cafeteria").RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/pos/events", async (long afterSequence, int? take, ServiceEventStore store, CancellationToken ct) =>
            Results.Ok(await store.GetAfterAsync(Math.Max(0, afterSequence), take ?? 100, ct)))
            .RequireAuthorization("CafeteriaPos");

        EmailEndpoints.Map(app);
    }
}