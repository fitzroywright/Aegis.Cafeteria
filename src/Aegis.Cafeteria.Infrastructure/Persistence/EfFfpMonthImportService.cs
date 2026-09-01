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
    private const string ImportedFfpTransactionEventType = "ImportedFfpTransaction";

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
            bool newTransaction = existingIds.Add(transaction.SourceId);
            if (newTransaction)
            {
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

                importedTransactions++;
                netImportedScanValue += transaction.ScanValue;
            }

            Guid eventId = ImportedEventId(transaction.SourceId);
            AccountEventRow? existingEvent = await db.AccountEvents
                .SingleOrDefaultAsync(row => row.Id == eventId, cancellationToken);

            if (existingEvent is null)
            {
                db.AccountEvents.Add
                (
                    new AccountEventRow
                    {
                          Id = eventId
                        , EventType = ImportedFfpTransactionEventType
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
            else if (existingEvent.EventType.StartsWith("Imported FFP", StringComparison.Ordinal)
                || existingEvent.EventType == ImportedFfpTransactionEventType)
            {
                existingEvent.EventType = ImportedFfpTransactionEventType;
                existingEvent.CustomerId = transaction.CustomerId;
                existingEvent.AccountYear = snapshot.Year;
                existingEvent.AccountMonth = snapshot.Month;
                existingEvent.Amount = transaction.ScanValue;
                existingEvent.PayloadJson = JsonSerializer.Serialize(transaction);
                existingEvent.OccurredAt = transaction.OccurredAt;
                existingEvent.Revision = importedAt.UtcDateTime.Ticks;
                existingEvent.UpdatedAt = importedAt;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

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
