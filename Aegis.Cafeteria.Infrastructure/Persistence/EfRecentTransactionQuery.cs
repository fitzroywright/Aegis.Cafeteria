namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfRecentTransactionQuery(IDbContextFactory<LocalCafeteriaDbContext> factory) : IRecentTransactionQuery
{
    public async Task<IReadOnlyList<RecentTransactionView>> GetTodayAsync(
        int take = 10,
        DateOnly? localDate = null,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));

        DateOnly date = localDate ?? DateOnly.FromDateTime(DateTime.Today);
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);

        // SQLite does not reliably translate DateTimeOffset range comparison/order operations.
        // Bound the SQL query by the monotonic revision index first, then apply the local calendar
        // date in memory. This avoids loading the entire completed-order history for every refresh.
        int scanLimit = Math.Max(2000, take * 100);
        var completedHeaders = await db.Orders.AsNoTracking()
            .Where(x => x.Status == (int)OrderStatus.Completed && x.CompletedAt.HasValue)
            .OrderByDescending(x => x.Revision)
            .Take(scanLimit)
            .Select(x => new { x.Id, x.CustomerId, x.CompletedAt, x.CashierUserId, x.TerminalId })
            .ToListAsync(cancellationToken);

        var selected = completedHeaders
            .Where(x => DateOnly.FromDateTime(x.CompletedAt!.Value.LocalDateTime) == date)
            .OrderByDescending(x => x.CompletedAt!.Value)
            .Take(take)
            .ToArray();

        if (selected.Length == 0) return [];

        Guid[] orderIds = selected.Select(x => x.Id).ToArray();
        Dictionary<Guid, OrderRow> details = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Where(x => orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        int[] customerIds = selected
            .Where(x => x.CustomerId != CashCustomerAccountViewFactory.CashCustomerId)
            .Select(x => x.CustomerId)
            .Distinct()
            .ToArray();
        Dictionary<int, string> names = customerIds.Length == 0
            ? new Dictionary<int, string>()
            : await db.Customers.AsNoTracking()
                .Where(x => customerIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return selected.Select(x =>
        {
            OrderRow order = details[x.Id];
            decimal card = order.Payments.Where(p => p.Method == (int)PaymentMethod.CafeteriaAccount).Sum(p => p.Amount);
            decimal cash = order.Payments.Where(p => p.Method == (int)PaymentMethod.Cash).Sum(p => p.Amount);
            decimal credit = order.Payments.Where(p => p.Method == (int)PaymentMethod.Credit).Sum(p => p.Amount);

            List<string> tenderParts = [];
            if (card > 0m) tenderParts.Add("Card");
            if (cash > 0m) tenderParts.Add("Cash");
            if (credit > 0m) tenderParts.Add("Credit");
            string tender = tenderParts.Count == 0 ? "Unknown" : string.Join(" + ", tenderParts);

            string customerName = x.CustomerId == CashCustomerAccountViewFactory.CashCustomerId
                ? "Cash Customer"
                : names.GetValueOrDefault(x.CustomerId, $"Customer {x.CustomerId}");
            return new RecentTransactionView(
                x.Id,
                x.CompletedAt!.Value,
                customerName,
                order.Items.Sum(i => i.UnitPrice * i.Quantity),
                card,
                cash,
                tender,
                x.CashierUserId ?? "legacy",
                x.TerminalId ?? "legacy");
        }).ToArray();
    }
}
