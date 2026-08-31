namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Microsoft.EntityFrameworkCore;

public sealed class EfRecentTransactionQuery(IDbContextFactory<LocalCafeteriaDbContext> factory) : IRecentTransactionQuery
{
    public async Task<IReadOnlyList<RecentTransactionView>> GetTodayAsync(
        int take = 10,
        DateOnly? localDate = null,
        CancellationToken cancellationToken = default)
    {
        if (take is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(take));

        var date = localDate ?? DateOnly.FromDateTime(DateTime.Today);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // SQLite does not reliably translate DateTimeOffset range comparison/order operations.
        // Keep the SQL part to a status/header projection, then apply the local calendar date and
        // sort in memory. Only the selected order details are loaded afterwards.
        var completedHeaders = await db.Orders.AsNoTracking()
            .Where(x => x.Status == (int)OrderStatus.Completed && x.CompletedAt.HasValue)
            .Select(x => new { x.Id, x.CustomerId, x.CompletedAt, x.CashierUserId, x.TerminalId })
            .ToListAsync(cancellationToken);

        var selected = completedHeaders
            .Where(x => DateOnly.FromDateTime(x.CompletedAt!.Value.LocalDateTime) == date)
            .OrderByDescending(x => x.CompletedAt!.Value)
            .Take(take)
            .ToArray();

        if (selected.Length == 0) return [];

        var orderIds = selected.Select(x => x.Id).ToArray();
        var details = await db.Orders.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Where(x => orderIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var customerIds = selected
            .Where(x => x.CustomerId != CashCustomerAccountViewFactory.CashCustomerId)
            .Select(x => x.CustomerId)
            .Distinct()
            .ToArray();
        var names = customerIds.Length == 0
            ? new Dictionary<int, string>()
            : await db.Customers.AsNoTracking()
                .Where(x => customerIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        return selected.Select(x =>
        {
            var order = details[x.Id];
            var cafeteria = order.Payments.Where(p => p.Method == (int)PaymentMethod.CafeteriaAccount).Sum(p => p.Amount);
            var cash = order.Payments.Where(p => p.Method == (int)PaymentMethod.Cash).Sum(p => p.Amount);
            var tender = cafeteria > 0m && cash > 0m ? "Account + Cash"
                : cafeteria > 0m ? "Account"
                : "Cash";
            var customerName = x.CustomerId == CashCustomerAccountViewFactory.CashCustomerId
                ? "Cash Customer"
                : names.GetValueOrDefault(x.CustomerId, $"Customer {x.CustomerId}");
            return new RecentTransactionView(
                x.Id,
                x.CompletedAt!.Value,
                customerName,
                order.Items.Sum(i => i.UnitPrice * i.Quantity),
                cafeteria,
                cash,
                tender,
                x.CashierUserId ?? "legacy",
                x.TerminalId ?? "legacy");
        }).ToArray();
    }
}
