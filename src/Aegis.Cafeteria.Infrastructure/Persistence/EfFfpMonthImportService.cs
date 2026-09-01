namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public sealed class EfFfpMonthImportService
(
      IFfpMonthImportSource source
    , IDbContextFactory<LocalCafeteriaDbContext> factory
) : IFfpMonthImportService
{
    public async Task<FfpMonthImportResult> ImportAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        FfpMonthImportSnapshot snapshot = await source.GetMonthAsync(year, month, cancellationToken);
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);

        HashSet<long> existingIds = (await db.FfpImportedTransactions
            .AsNoTracking()
            .Where(row => row.Year == year && row.Month == month)
            .Select(row => row.SourceId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        DateTimeOffset importedAt = DateTimeOffset.UtcNow;
        int importedTransactions = 0;
        decimal netImportedScanValue = 0m;

        foreach (ExternalFfpTransactionSnapshot transaction in snapshot.Transactions)
        {
            if (!existingIds.Add(transaction.SourceId))
            {
                continue;
            }

            db.FfpImportedTransactions.Add
            (
                new FfpImportedTransactionRow
                {
                      SourceId = transaction.SourceId
                    , CustomerId = transaction.CustomerId
                    , Year = snapshot.Year
                    , Month = snapshot.Month
                    , OccurredAt = transaction.OccurredAt
                    , IdName = transaction.IdName
                    , Description = transaction.Description
                    , ScanValue = transaction.ScanValue
                    , UserName = transaction.UserName
                    , ScanCode = transaction.ScanCode
                    , ScanType = transaction.ScanType
                    , ImportedAt = importedAt
                }
            );

            Guid eventId = ImportedEventId(transaction.SourceId);
            bool eventExists = await db.AccountEvents.AnyAsync(row => row.Id == eventId, cancellationToken);
            if (!eventExists)
            {
                string detail = string.IsNullOrWhiteSpace(transaction.Description)
                    ? transaction.IdName
                    : $"{transaction.IdName} - {transaction.Description}";
                string eventType = $"Imported FFP - {detail}";
                if (eventType.Length > 100)
                {
                    eventType = eventType[..100];
                }

                db.AccountEvents.Add
                (
                    new AccountEventRow
                    {
                          Id = eventId
                        , EventType = eventType
                        , CustomerId = transaction.CustomerId
                        , AccountYear = snapshot.Year
                        , AccountMonth = snapshot.Month
                        , Amount = transaction.ScanValue
                        , PayloadJson = JsonSerializer.Serialize(transaction)
                        , OccurredAt = transaction.OccurredAt
                        , Revision = importedAt.UtcDateTime.Ticks
                        , UpdatedAt = importedAt
                    }
                );
            }

            importedTransactions++;
            netImportedScanValue += transaction.ScanValue;
        }

        if (importedTransactions > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return new FfpMonthImportResult
        (
              snapshot.Year
            , snapshot.Month
            , snapshot.ThroughAt
            , importedTransactions
            , snapshot.Accounts.Count
            , netImportedScanValue
        );
    }

    private static Guid ImportedEventId(long sourceId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"FFP-IMPORT:{sourceId}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
