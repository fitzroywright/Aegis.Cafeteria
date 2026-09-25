using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Services.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace Aegis.Cafeteria.Services;

internal static class ImportToolsEndpoints
{
    internal static void Map(WebApplication app)
    {
        app.MapPost("/api/management/import-tools/products", ImportProductsAsync)
            .RequireAuthorization("CafeteriaManagement");

    }

    private static async Task<IResult> ImportProductsAsync
    (
          HttpContext http
        , ILunchItemBootstrapSource source
        , FfpIntegrationOptions options
        , ServiceEventStore store
        , CancellationToken ct
    )
    {
        string actor = CafeteriaActor.Require(http);
        IReadOnlyList<ExternalProductSnapshot> sourceItems = await source.GetLunchItemsAsync(ct);

        string[] reservedCodes =
        [
              options.CreditLimitLunchItemCode
            , options.SubsidyLunchItemCode
        ];

        ExternalProductSnapshot[] importable = sourceItems
            .Where(item => !reservedCodes.Contains(item.Code?.Trim(), StringComparer.OrdinalIgnoreCase))
            .Where(item => item.ProductId > 0)
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => item.Price >= 0m)
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        int created = 0;
        int existing = 0;
        DateTimeOffset importedAt = DateTimeOffset.UtcNow;

        foreach (ExternalProductSnapshot item in importable)
        {
            Guid operationId = DeterministicOperationId($"PRODUCT-IMPORT:{item.ProductId}:{item.Name.Trim().ToUpperInvariant()}");
            if (await store.HasOperationAsync(operationId, ct))
            {
                existing++;
                continue;
            }

            ProductCatalogImportServiceEvent payload = new
            (
                  operationId
                , item.ProductId
                , item.Code
                , item.Name.Trim()
                , item.Price
                , actor
                , importedAt
            );

            await store.AppendAsync
            (
                  operationId
                , CafeteriaServiceEventKind.ProductCatalogImport
                , 0
                , importedAt
                , payload
                , ct
            );

            created++;
        }

        int ignored = sourceItems.Count - importable.Length;

        return Results.Ok
        (
            new
            {
                  SourceItems = sourceItems.Count
                , ImportEventsCreated = created
                , ExistingImportEvents = existing
                , IgnoredItems = ignored
                , ImportedAt = importedAt
            }
        );
    }

    private static Guid DeterministicOperationId(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }
}
