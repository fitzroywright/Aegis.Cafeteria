using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;
using System.Security.Cryptography;
using System.Text;

namespace Aegis.Cafeteria.Services;

internal static class MigrationEndpoints
{
    internal static void Map(WebApplication app)
    {
        if (!app.Configuration.GetValue("Migration:Enabled", false))
            return;

        app.MapPost("/api/migration/legacy-month", ImportLegacyMonthAsync)
            .RequireAuthorization("CafeteriaManagement");

        app.MapPost("/api/migration/products", ImportProductsAsync)
            .RequireAuthorization("CafeteriaManagement");
    }

    private static async Task<IResult> ImportProductsAsync(
        ProductMigrationRequest request,
        HttpContext http,
        ServiceEventStore store,
        CancellationToken ct)
    {
        string actor = CafeteriaActor.Require(http);
        DateTimeOffset importedAt = DateTimeOffset.UtcNow;

        ProductMigrationItem[] importable = request.Products
            .Where(item => item.ProductId > 0)
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => item.Price >= 0m)
            .GroupBy(
                item => item.Name.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        int created = 0;
        int existing = 0;

        foreach (ProductMigrationItem item in importable)
        {
            Guid operationId = ProductOperationId(item);

            if (await store.HasOperationAsync(operationId, ct))
            {
                existing++;
                continue;
            }

            ProductCatalogImportServiceEvent payload = new(
                operationId,
                item.ProductId,
                item.Code?.Trim() ?? string.Empty,
                item.Name.Trim(),
                item.Price,
                actor,
                importedAt);

            await store.AppendAsync(
                operationId,
                CafeteriaServiceEventKind.ProductCatalogImport,
                0,
                importedAt,
                payload,
                ct);

            created++;
        }

        return Results.Ok(new
        {
            SourceProducts = request.Products.Count,
            ImportableProducts = importable.Length,
            ImportEventsCreated = created,
            ExistingImportEvents = existing,
            IgnoredProducts = request.Products.Count - importable.Length,
            ImportedAt = importedAt
        });
    }

    private static Guid ProductOperationId(ProductMigrationItem item)
        => DeterministicOperationId(
            $"PRODUCT-IMPORT:{item.ProductId}:{item.Name.Trim().ToUpperInvariant()}");

    private static async Task<IResult> ImportLegacyMonthAsync(
        LegacyMonthMigrationRequest request,
        HttpContext http,
        ServiceEventStore store,
        CancellationToken ct)
    {
        if (request.Year is < 2000 or > 9999 || request.Month is < 1 or > 12)
            return Results.BadRequest("A valid migration year and month are required.");

        string actor = CafeteriaActor.Require(http);
        int created = 0;
        int existing = 0;
        decimal netImportedScanValue = 0m;
        DateTimeOffset importedAt = DateTimeOffset.UtcNow;

        foreach (LegacyTransactionMigrationItem transaction in request.Transactions)
        {
            Guid operationId = DeterministicOperationId(
                $"LEGACY-IMPORT:{transaction.SourceId}");

            if (await store.HasOperationAsync(operationId, ct))
            {
                existing++;
                continue;
            }

            LegacyTransactionImportServiceEvent payload = new(
                operationId,
                transaction.SourceId,
                transaction.CustomerId,
                request.Year,
                request.Month,
                transaction.OccurredAt,
                transaction.IdName,
                transaction.Description,
                transaction.ScanValue,
                transaction.UserName,
                transaction.ScanCode,
                transaction.ScanType,
                actor,
                importedAt);

            await store.AppendAsync(
                operationId,
                CafeteriaServiceEventKind.LegacyTransactionImport,
                transaction.CustomerId,
                transaction.OccurredAt,
                payload,
                ct);

            created++;
            netImportedScanValue += transaction.ScanValue;
        }

        return Results.Ok(new
        {
            request.Year,
            request.Month,
            request.ThroughAt,
            ImportEventsCreated = created,
            ExistingImportEvents = existing,
            SourceTransactions = request.Transactions.Count,
            SourceAccounts = request.Accounts.Count,
            NetImportedScanValue = netImportedScanValue,
            ImportedAt = importedAt
        });
    }

    private static Guid DeterministicOperationId(string value)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    internal sealed record ProductMigrationRequest(
        IReadOnlyList<ProductMigrationItem> Products);

    internal sealed record ProductMigrationItem(
        int ProductId,
        string Code,
        string Name,
        decimal Price);

    internal sealed record LegacyMonthMigrationRequest(
        int Year,
        int Month,
        DateTimeOffset ThroughAt,
        IReadOnlyList<LegacyTransactionMigrationItem> Transactions,
        IReadOnlyList<LegacyAccountMigrationItem> Accounts);

    internal sealed record LegacyTransactionMigrationItem(
        long SourceId,
        int CustomerId,
        DateTimeOffset OccurredAt,
        string IdName,
        string Description,
        decimal ScanValue,
        string UserName,
        string ScanCode,
        int ScanType);

    internal sealed record LegacyAccountMigrationItem(
        int CustomerId,
        decimal CreditLimit,
        decimal AvailableBalance);
}
