using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;

namespace Aegis.Cafeteria.Services;

internal static class FfpEndpoints
{
    internal static void Map(WebApplication app)
    {
        MapReferenceAndIntegrationRoutes(app);

        // Compatibility aliases for older POS builds. New clients use the neutral Cafeteria-owned routes.
        app.MapGet("/api/ffp/customers", GetCustomersAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/ffp/holidays", GetHolidaysAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/ffp/lunch-items", GetProductsAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/ffp/accounts", GetAccountsAsync).RequireAuthorization("CafeteriaPos");
        app.MapPost("/api/ffp/ledger", PublishLedgerEndpointAsync).RequireAuthorization("CafeteriaPos");
    }

    private static void MapReferenceAndIntegrationRoutes(WebApplication app)
    {
        app.MapGet("/api/reference/customers", GetCustomersAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/reference/holidays", GetHolidaysAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/reference/products", GetProductsAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/integration/accounts", GetAccountsAsync).RequireAuthorization("CafeteriaPos");
        app.MapPost("/api/integration/ledger", PublishLedgerEndpointAsync).RequireAuthorization("CafeteriaPos");
    }

    private static async Task<IResult> GetHolidaysAsync(int year, int month, IWorkforceReferenceSource source, CancellationToken ct)
        => Results.Ok(await source.GetHolidaysAsync(year, month, ct));

    private static async Task<IResult> GetProductsAsync(ILunchItemBootstrapSource source, CancellationToken ct)
        => Results.Ok(await source.GetLunchItemsAsync(ct));

    private static async Task<IResult> GetAccountsAsync(IExternalAccountingGateway gateway, CancellationToken ct)
        => Results.Ok(await gateway.GetAccountSummariesAsync(ct));

    private static Task<IResult> PublishLedgerEndpointAsync(
        ExternalLedgerEntry entry,
        IExternalAccountingGateway gateway,
        ServiceEventStore store,
        HttpContext http,
        CancellationToken ct)
        => PublishLedgerAsync(http.RequestServices.GetRequiredService<IWebHostEnvironment>(), entry, gateway, store, ct);

    private static async Task<IResult> GetCustomersAsync(IWorkforceReferenceSource source, CustomerCreditPolicyStore policies, CancellationToken ct)
    {
        IReadOnlyList<ExternalCustomerSnapshot> customers = await source.GetCustomersAsync(ct);
        IReadOnlyDictionary<int, decimal> limits = await policies.GetAllAdditionalCreditLimitsAsync(ct);
        return Results.Ok(customers.Select(x => x with { AdditionalCreditLimit = limits.GetValueOrDefault(x.CustomerId, 0m) }).ToArray());
    }

    private static async Task<IResult> PublishLedgerAsync(IWebHostEnvironment environment, ExternalLedgerEntry entry, IExternalAccountingGateway gateway, ServiceEventStore store, CancellationToken ct)
    {
        DateTimeOffset? latestResetAt = await store.GetLatestDevelopmentResetAtAsync(ct);
        if (environment.IsDevelopment() && latestResetAt is DateTimeOffset resetAt && entry.OccurredAt <= resetAt)
            return Results.Ok(new { ignored = true, reason = "Entry predates the latest Development reset." });

        await gateway.PublishLedgerEntryAsync(entry, ct);
        return Results.Ok();
    }
}