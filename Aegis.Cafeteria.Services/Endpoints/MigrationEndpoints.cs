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
        {
            return;
        }

        app.MapPost("/api/migration/legacy-month", ImportLegacyMonthAsync)
            .RequireAuthorization("CafeteriaManagement");
    }

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
