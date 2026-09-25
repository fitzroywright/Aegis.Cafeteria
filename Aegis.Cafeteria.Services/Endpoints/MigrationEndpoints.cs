using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Services.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace Aegis.Cafeteria.Services;

internal static class MigrationEndpoints
{
    internal static void Map(WebApplication app)
    {
        if (!app.Configuration.GetValue("Migration:Enabled", false))
        {
            return;
        }

        app.MapPost("/api/migration/legacy-month", ImportLegacyMonthAsync)
            .RequireAuthorization("CafeteriaManagement");

        app.MapGet("/api/migration/product-bootstrap", PreviewProductBootstrapAsync)
            .RequireAuthorization("CafeteriaManagement");

        app.MapPost("/api/migration/product-bootstrap", ExecuteProductBootstrapAsync)
            .RequireAuthorization("CafeteriaManagement");
    }

    private static async Task<IResult> PreviewProductBootstrapAsync
    (
          ILunchItemBootstrapSource source
        , FfpIntegrationOptions options
        , ServiceEventStore store
        , CancellationToken ct
    )
    {
        ProductBootstrapPlan plan = await BuildProductBootstrapPlanAsync(source, options, store, ct);
        return Results.Ok
        (
            new
            {
                  plan.SourceItems
                , plan.ImportableItems
                , plan.ExistingImportEvents
                , plan.IgnoredItems
                , WouldCreate = plan.ItemsToCreate.Count
            }
        );
    }

    private static async Task<IResult> ExecuteProductBootstrapAsync
    (
          HttpContext http
        , ILunchItemBootstrapSource source
        , FfpIntegrationOptions options
        , ServiceEventStore store
        , CancellationToken ct
    )
    {
        ProductBootstrapPlan plan = await BuildProductBootstrapPlanAsync(source, options, store, ct);
        string actor = CafeteriaActor.Require(http);
        DateTimeOffset importedAt = DateTimeOffset.UtcNow;

        foreach (ExternalProductSnapshot item in plan.ItemsToCreate)
        {
            Guid operationId = ProductOperationId(item);
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
        }

        return Results.Ok
        (
            new
            {
                  plan.SourceItems
                , plan.ImportableItems
                , ImportEventsCreated = plan.ItemsToCreate.Count
                , plan.ExistingImportEvents
                , plan.IgnoredItems
                , ImportedAt = importedAt
            }
        );
    }

    private static async Task<ProductBootstrapPlan> BuildProductBootstrapPlanAsync
    (
          ILunchItemBootstrapSource source
        , FfpIntegrationOptions options
        , ServiceEventStore store
        , CancellationToken ct
    )
    {
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

        List<ExternalProductSnapshot> create = [];
        int existing = 0;

        foreach (ExternalProductSnapshot item in importable)
        {
            if (await store.HasOperationAsync(ProductOperationId(item), ct))
            {
                existing++;
            }
            else
            {
                create.Add(item);
            }
        }

        return new ProductBootstrapPlan
        (
              sourceItems.Count
            , importable.Length
            , existing
            , sourceItems.Count - importable.Length
            , create
        );
    }

    private static Guid ProductOperationId(ExternalProductSnapshot item)
        => DeterministicOperationId($"PRODUCT-IMPORT:{item.ProductId}:{item.Name.Trim().ToUpperInvariant()}");

    private static async Task<IResult> ImportLegacyMonthAsync
    (
          LegacyMonthMigrationRequest request
        , HttpContext http
        , ServiceEventStore store
        , CancellationToken ct
    )
    {
        if (request.Year is < 2000 or > 9999 || request.Month is < 1 or > 12)
        {
            return Results.BadRequest("A valid migration year and month are required.");
        }

        string actor = CafeteriaActor.Require(http);
        int created = 0;
        int existing = 0;
        decimal netImportedScanValue = 0m;
        DateTimeOffset importedAt = DateTimeOffset.UtcNow;

        foreach (LegacyTransactionMigrationItem transaction in request.Transactions)
        {
            Guid operationId = DeterministicOperationId($"LEGACY-IMPORT:{transaction.SourceId}");

            if (await store.HasOperationAsync(operationId, ct))
            {
                existing++;
                continue;
            }

            LegacyTransactionImportServiceEvent payload = new
            (
                  operationId
                , transaction.SourceId
                , transaction.CustomerId
                , request.Year
                , request.Month
                , transaction.OccurredAt
                , transaction.IdName
                , transaction.Description
                , transaction.ScanValue
                , transaction.UserName
                , transaction.ScanCode
                , transaction.ScanType
                , actor
                , importedAt
            );

            await store.AppendAsync
            (
                  operationId
                , CafeteriaServiceEventKind.LegacyTransactionImport
                , transaction.CustomerId
                , transaction.OccurredAt
                , payload
                , ct
            );

            created++;
            netImportedScanValue += transaction.ScanValue;
        }

        return Results.Ok
        (
            new
            {
                  request.Year
                , request.Month
                , request.ThroughAt
                , ImportEventsCreated = created
                , ExistingImportEvents = existing
                , SourceTransactions = request.Transactions.Count
                , SourceAccounts = request.Accounts.Count
                , NetImportedScanValue = netImportedScanValue
                , ImportedAt = importedAt
            }
        );
    }

    private static Guid DeterministicOperationId(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private sealed record ProductBootstrapPlan
    (
          int SourceItems
        , int ImportableItems
        , int ExistingImportEvents
        , int IgnoredItems
        , IReadOnlyList<ExternalProductSnapshot> ItemsToCreate
    );

    internal sealed record LegacyMonthMigrationRequest
    (
          int Year
        , int Month
        , DateTimeOffset ThroughAt
        , IReadOnlyList<LegacyTransactionMigrationItem> Transactions
        , IReadOnlyList<LegacyAccountMigrationItem> Accounts
    );

    internal sealed record LegacyTransactionMigrationItem
    (
          long SourceId
        , int CustomerId
        , DateTimeOffset OccurredAt
        , string IdName
        , string Description
        , decimal ScanValue
        , string UserName
        , string ScanCode
        , int ScanType
    );

    internal sealed record LegacyAccountMigrationItem
    (
          int CustomerId
        , decimal CreditLimit
        , decimal AvailableBalance
    );
}
