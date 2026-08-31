namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Microsoft.EntityFrameworkCore;

public sealed class EfWeeklyTransactionSummaryQuery(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    ICustomerAccountQuery accountQuery) : IWeeklyTransactionSummaryQuery, IWeeklySummaryDispatchStore
{
    public async Task<IReadOnlyList<WeeklyTransactionSummary>> GetAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken cancellationToken = default)
    {
        var start = new DateTimeOffset(weekStart.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(weekStart.ToDateTime(TimeOnly.MinValue)));
        var endExclusiveDate = weekEnd.AddDays(1);
        var end = new DateTimeOffset(endExclusiveDate.ToDateTime(TimeOnly.MinValue), TimeZoneInfo.Local.GetUtcOffset(endExclusiveDate.ToDateTime(TimeOnly.MinValue)));
        await using var db = await factory.CreateDbContextAsync(cancellationToken);

        // SQLite cannot translate DateTimeOffset relational ordering/comparison reliably. Keep the SQL filter
        // to status only, materialize the bounded operational set, then compare/sort DateTimeOffset in memory.
        var completed = await db.Orders.AsNoTracking().Include(x => x.Payments).Include(x => x.Refunds).ThenInclude(x => x.Allocations)
            .Where(x => x.Status == (int)OrderStatus.Completed).ToListAsync(cancellationToken);
        var orders = completed.Where(x => x.CompletedAt.HasValue && x.CompletedAt.Value >= start && x.CompletedAt.Value < end).ToArray();
        if (orders.Length == 0) return [];
        var customerIds = orders.Select(x => x.CustomerId).Distinct().ToArray();
        var customers = await db.Customers.AsNoTracking().Where(x => customerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, cancellationToken);
        var result = new List<WeeklyTransactionSummary>();
        foreach (var group in orders.GroupBy(x => x.CustomerId))
        {
            var account = await accountQuery.GetCurrentAsync(group.Key, cancellationToken);
            if (!customers.TryGetValue(group.Key, out var customer)) continue;
            var refunds = group.SelectMany(x => x.Refunds)
                .Where(x => x.RefundedAt >= start && x.RefundedAt < end).Sum(x => x.Allocations.Sum(a => a.Amount));
            result.Add(new WeeklyTransactionSummary(
                group.Key, customer.Name, weekStart, weekEnd, group.Count(), group.Sum(x => x.Items.Sum(i => i.UnitPrice * i.Quantity)),
                group.Sum(x => x.Payments.Where(p => p.Method == (int)PaymentMethod.CafeteriaAccount).Sum(p => p.Amount)),
                group.Sum(x => x.Payments.Where(p => p.Method == (int)PaymentMethod.Cash).Sum(p => p.Amount)),
                refunds, account?.FinancialStatus.TotalAvailableToSpend ?? 0m));
        }
        return result;
    }

    public async Task<bool> WasSentAsync(int customerId, DateOnly weekStart, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.WeeklySummaryDispatches.AsNoTracking().AnyAsync(x => x.CustomerId == customerId && x.WeekStart == weekStart, cancellationToken);
    }

    public async Task MarkSentAsync(int customerId, DateOnly weekStart, Guid messageId, DateTimeOffset sentAt, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        if (!await db.WeeklySummaryDispatches.AnyAsync(x => x.CustomerId == customerId && x.WeekStart == weekStart, cancellationToken))
        {
            db.WeeklySummaryDispatches.Add(new Models.WeeklySummaryDispatchRow { CustomerId = customerId, WeekStart = weekStart, MessageId = messageId, SentAt = sentAt });
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
