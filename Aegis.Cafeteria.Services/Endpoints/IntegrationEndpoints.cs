using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;

namespace Aegis.Cafeteria.Services;

internal static class IntegrationEndpoints
{
    internal static void Map(WebApplication app)
    {
        MapReferenceAndIntegrationRoutes(app);
    }

    private static void MapReferenceAndIntegrationRoutes(WebApplication app)
    {
        app.MapGet("/api/reference/customers", GetCustomersAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/reference/holidays", GetHolidaysAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/reference/products", GetProductsAsync).RequireAuthorization("CafeteriaPos");
        app.MapPost("/api/integration/ledger", PublishLedgerEndpointAsync).RequireAuthorization("CafeteriaPos");
    }

    private static async Task<IResult> GetHolidaysAsync(int year, int month, IWorkforceReferenceSource source, CancellationToken ct)
        => Results.Ok(await source.GetHolidaysAsync(year, month, ct));

    private static async Task<IResult> GetProductsAsync(ILunchItemBootstrapSource source, CancellationToken ct)
        => Results.Ok(await source.GetLunchItemsAsync(ct));

    private static Task<IResult> PublishLedgerEndpointAsync(
        ExternalLedgerEntry entry,
        ServiceEventStore store,
        HttpContext http,
        CancellationToken ct)
        => PublishLedgerAsync(http.RequestServices.GetRequiredService<IWebHostEnvironment>(), entry, store, ct);

    private static async Task<IResult> GetCustomersAsync(IWorkforceReferenceSource source, CustomerCreditPolicyStore policies, CancellationToken ct)
    {
        IReadOnlyList<ExternalCustomerSnapshot> customers = await source.GetCustomersAsync(ct);
        IReadOnlyDictionary<int, decimal> limits = await policies.GetAllAdditionalCreditLimitsAsync(ct);
        return Results.Ok(customers.Select(x => x with { AdditionalCreditLimit = limits.GetValueOrDefault(x.CustomerId, 0m) }).ToArray());
    }

    private static async Task<IResult> PublishLedgerAsync(
        IWebHostEnvironment environment,
        ExternalLedgerEntry entry,
        ServiceEventStore store,
        CancellationToken ct)
    {
        DateTimeOffset? latestResetAt = await store.GetLatestDevelopmentResetAtAsync(ct);
        if (environment.IsDevelopment() && latestResetAt is DateTimeOffset resetAt && entry.OccurredAt <= resetAt)
            return Results.Ok(new { ignored = true, reason = "Entry predates the latest Development reset." });

        await store.AppendAsync(
            entry.OperationId,
            Aegis.Cafeteria.Domain.Services.CafeteriaServiceEventKind.ExternalLedgerEntry,
            entry.CustomerId,
            entry.OccurredAt,
            entry,
            ct);

        return Results.Ok();
    }
}