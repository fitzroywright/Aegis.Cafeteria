namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfExternalPublicationStatusProvider(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    CafeteriaServicesOptions options) : IExternalPublicationStatusProvider
{
    public async Task<ExternalPublicationStatus> GetAsync(
        CancellationToken cancellationToken = default)
    {
        if (!options.Enabled)
            return new(false, 0, 0, null, null);

        await using LocalCafeteriaDbContext database =
            await factory.CreateDbContextAsync(cancellationToken);

        List<ExternalLedgerOutboxRow> rows = await database.ExternalLedgerOutbox
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        int pending = rows.Count(row =>
            row.State is ExternalOutboxState.Pending
                or ExternalOutboxState.Failed
                or ExternalOutboxState.Processing);

        int poisoned = rows.Count(row =>
            row.State == ExternalOutboxState.Poisoned);

        DateTimeOffset? last = rows
            .Where(row =>
                row.State == ExternalOutboxState.Succeeded
                && row.LastAttemptAt.HasValue)
            .Select(row => row.LastAttemptAt)
            .OrderByDescending(value => value)
            .FirstOrDefault();

        string? error = rows
            .Where(row =>
                row.State is ExternalOutboxState.Failed
                    or ExternalOutboxState.Poisoned)
            .OrderByDescending(row => row.LastAttemptAt)
            .Select(row => row.LastError)
            .FirstOrDefault();

        return new(true, pending, poisoned, last, error);
    }
}
