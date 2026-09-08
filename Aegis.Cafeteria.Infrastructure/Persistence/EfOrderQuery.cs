namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Orders;
using Microsoft.EntityFrameworkCore;

public sealed class EfOrderQuery(IDbContextFactory<LocalCafeteriaDbContext> factory) : IOrderQuery
{
    public async Task<Order?> GetAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        if (orderId == Guid.Empty) throw new ArgumentException("Order ID is required.", nameof(orderId));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Refunds).ThenInclude(x => x.Items)
            .Include(x => x.Refunds).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == orderId, cancellationToken);
        return row is null ? null : DomainReadMapper.ToDomain(row);
    }

    public async Task<IReadOnlyList<Order>> GetRecentCompletedAsync(int? customerId = null, int take = 50, CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(take));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var query = db.Orders.AsNoTracking().AsSplitQuery()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Refunds).ThenInclude(x => x.Items)
            .Include(x => x.Refunds).ThenInclude(x => x.Allocations)
            .Where(x => x.Status == (int)OrderStatus.Completed && x.CompletedAt.HasValue);
        if (customerId.HasValue) query = query.Where(x => x.CustomerId == customerId.Value);
        var rows = await query.OrderByDescending(x => x.CompletedAt).Take(take).ToArrayAsync(cancellationToken);
        return rows.Select(DomainReadMapper.ToDomain).ToArray();
    }
}
