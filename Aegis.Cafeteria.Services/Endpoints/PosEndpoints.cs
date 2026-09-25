using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;
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

        app.MapPost("/api/pos/transactions", PublishTransactionAsync)
            .RequireAuthorization("CafeteriaPos");

        EmailEndpoints.Map(app);
    
    private static async Task<IResult> PublishTransactionAsync(
        PosTransactionEvent transaction,
        ServiceEventStore store,
        CancellationToken ct)
    {
        CafeteriaServiceEventEnvelope saved = await store.AppendAsync(
            transaction.EventId,
            CafeteriaServiceEventKind.PosTransaction,
            transaction.CustomerId,
            transaction.OccurredAt,
            transaction,
            ct);

        return Results.Ok(saved);
    }
}
}
