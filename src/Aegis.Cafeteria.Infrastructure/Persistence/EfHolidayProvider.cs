namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

public sealed class EfHolidayProvider(IDbContextFactory<LocalCafeteriaDbContext> factory) : IHolidayProvider
{
    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        return await db.Holidays.AsNoTracking()
            .Where(x => x.Date >= start && x.Date < end)
            .Select(x => x.Date)
            .ToArrayAsync(cancellationToken);
    }
}
