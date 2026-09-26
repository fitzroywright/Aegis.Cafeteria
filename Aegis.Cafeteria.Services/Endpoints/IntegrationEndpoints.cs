using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Services.Persistence;
using Aegis.Cafeteria.Infrastructure.Identity;
using Common.Security.Models;

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
        app.MapPost("/api/integration/ledger", PublishLedgerEndpointAsync).RequireAuthorization("CafeteriaPos");
    }

    private static async Task<IResult> GetHolidaysAsync(int year, int month, IWorkforceReferenceSource source, CancellationToken ct)
        => Results.Ok(await source.GetHolidaysAsync(year, month, ct));


    private static Task<IResult> PublishLedgerEndpointAsync(
        ExternalLedgerEntry entry,
        ServiceEventStore store,
        CancellationToken ct)
        => PublishLedgerAsync(entry, store, ct);

    private static async Task<IResult> GetCustomersAsync(
        IWorkforceReferenceSource source,
        CustomerCreditPolicyStore policies,
        DirectoryProfileCache directoryCache,
        CancellationToken ct)
    {
        IReadOnlyList<ExternalCustomerSnapshot> customers = await source.GetCustomersAsync(ct);
        IReadOnlyDictionary<int, decimal> limits = await policies.GetAllAdditionalCreditLimitsAsync(ct);

        ExternalCustomerSnapshot[] enriched = customers.Select(customer =>
        {
            DirectoryUserProfile? metadata = directoryCache.Get(customer.LoginName);
            return customer with
            {
                AdditionalCreditLimit = limits.GetValueOrDefault(customer.CustomerId, 0m),
                EmailAddress = metadata?.EmailAddress ?? customer.EmailAddress,
                Department = metadata?.Department ?? customer.Department,
                Manager = metadata?.Manager ?? customer.Manager,
                Extension = metadata?.Extension ?? customer.Extension
            };
        }).ToArray();

        return Results.Ok(enriched);
    }

    private static async Task<IResult> PublishLedgerAsync(
        ExternalLedgerEntry entry,
        ServiceEventStore store,
        CancellationToken ct)
    {
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