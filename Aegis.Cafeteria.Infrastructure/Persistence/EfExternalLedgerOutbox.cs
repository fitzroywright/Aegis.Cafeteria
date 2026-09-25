namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfExternalLedgerOutbox(IDbContextFactory<LocalCafeteriaDbContext> factory) : IExternalLedgerOutbox
{
    public async Task EnqueueAsync(ExternalLedgerEntry entry, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (await db.ExternalLedgerOutbox.AnyAsync(x => x.OperationId == entry.OperationId, cancellationToken)) return;
        db.ExternalLedgerOutbox.Add(new ExternalLedgerOutboxRow
        {
            OperationId=entry.OperationId, EmployeeId=entry.CustomerId, Kind=(int)entry.Kind,
            OccurredAt=entry.OccurredAt, Description=entry.Description, ScanValue=entry.ScanValue,
            CreditLimit=entry.CreditLimit, AvailableBalance=entry.AvailableBalance, ScanCode=entry.ScanCode,
            UserName=entry.UserName, IdName=entry.IdName, Code=entry.Code, CreatedBy=entry.CreatedBy,
            State=ExternalOutboxState.Pending, CreatedAt=DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
