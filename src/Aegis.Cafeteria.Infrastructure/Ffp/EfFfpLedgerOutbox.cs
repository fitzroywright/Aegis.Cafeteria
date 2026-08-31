namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfFfpLedgerOutbox(IDbContextFactory<LocalCafeteriaDbContext> factory) : IExternalLedgerOutbox
{
    public async Task EnqueueAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.FfpLedgerOutbox.AnyAsync(x => x.OperationId == entry.OperationId, cancellationToken)) return;
        db.FfpLedgerOutbox.Add(new FfpLedgerOutboxRow
        {
            OperationId=entry.OperationId, EmployeeId=entry.CustomerId, Kind=(int)entry.Kind,
            OccurredAt=entry.OccurredAt, Description=entry.Description, ScanValue=entry.ScanValue,
            CreditLimit=entry.CreditLimit, AvailableBalance=entry.AvailableBalance, ScanCode=entry.ScanCode,
            UserName=entry.UserName, IdName=entry.IdName,
            State=FfpOutboxState.Pending, CreatedAt=DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
