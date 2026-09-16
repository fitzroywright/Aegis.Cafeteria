using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;

namespace Aegis.Cafeteria.Services;

internal static class FfpEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapGet("/api/ffp/customers", GetCustomersAsync).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/ffp/holidays", async (int year, int month, IWorkforceReferenceSource source, CancellationToken ct) => Results.Ok(await source.GetHolidaysAsync(year, month, ct))).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/ffp/lunch-items", async (ILunchItemBootstrapSource source, CancellationToken ct) => Results.Ok(await source.GetLunchItemsAsync(ct))).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/ffp/month-import", async (int year, int month, IFfpMonthImportSource source, CancellationToken ct) => Results.Ok(await source.GetMonthAsync(year, month, ct))).RequireAuthorization("CafeteriaPos");
        app.MapGet("/api/ffp/accounts", async (IExternalAccountingGateway gateway, CancellationToken ct) => Results.Ok(await gateway.GetAccountSummariesAsync(ct))).RequireAuthorization("CafeteriaPos");
        app.MapPost("/api/ffp/ledger", (ExternalLedgerEntry entry, IExternalAccountingGateway gateway, ServiceEventStore store, CancellationToken ct) => PublishLedgerAsync(app, entry, gateway, store, ct)).RequireAuthorization("CafeteriaPos");
    }

    private static async Task<IResult> GetCustomersAsync(IWorkforceReferenceSource source, CustomerCreditPolicyStore policies, CancellationToken ct)
    {
        IReadOnlyList<ExternalCustomerSnapshot> customers = await source.GetCustomersAsync(ct);
        IReadOnlyDictionary<int, decimal> limits = await policies.GetAllAdditionalCreditLimitsAsync(ct);
        return Results.Ok(customers.Select(x => x with { AdditionalCreditLimit = limits.GetValueOrDefault(x.CustomerId, 0m) }).ToArray());
    }

    private static async Task<IResult> PublishLedgerAsync(WebApplication app, ExternalLedgerEntry entry, IExternalAccountingGateway gateway, ServiceEventStore store, CancellationToken ct)
    {
        DateTimeOffset? latestResetAt = await store.GetLatestDevelopmentResetAtAsync(ct);
        if (app.Environment.IsDevelopment() && latestResetAt is DateTimeOffset resetAt && entry.OccurredAt <= resetAt)
            return Results.Ok(new { ignored = true, reason = "Entry predates the latest Development reset." });

        await gateway.PublishLedgerEntryAsync(entry, ct);
        return Results.Ok();
    }
}