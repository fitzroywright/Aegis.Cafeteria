namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Microsoft.EntityFrameworkCore;

public sealed class EfEmployeeParticipationQuery(
    IDbContextFactory<LocalCafeteriaDbContext> factory) : IEmployeeParticipationQuery
{
    public async Task<EmployeeParticipationWeekView> GetWeekAsync
    (
        DateOnly monday,
        CancellationToken cancellationToken = default
    )
    {
        DateOnly friday = monday.AddDays(4);
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);

        int eligibleEmployeeCount = await db.Customers
            .AsNoTracking()
            .CountAsync(customer => customer.IsEligibleForCredit, cancellationToken);

        List<(int CustomerId, DateTimeOffset CompletedAt)> completedOrders = await db.Orders
            .AsNoTracking()
            .Where(order =>
                order.Status == (int)OrderStatus.Completed
                && order.CompletedAt.HasValue)
            .Select(order => new ValueTuple<int, DateTimeOffset>
            (
                order.CustomerId,
                order.CompletedAt!.Value
            ))
            .ToListAsync(cancellationToken);

        HashSet<int> eligibleCustomerIds = await db.Customers
            .AsNoTracking()
            .Where(customer => customer.IsEligibleForCredit)
            .Select(customer => customer.Id)
            .ToHashSetAsync(cancellationToken);

        List<EmployeeParticipationDayView> days = [];
        for (int offset = 0; offset < 5; offset++)
        {
            DateOnly day = monday.AddDays(offset);
            int employeesServed = completedOrders
                .Where(order =>
                    eligibleCustomerIds.Contains(order.CustomerId)
                    && DateOnly.FromDateTime(order.CompletedAt.LocalDateTime) == day)
                .Select(order => order.CustomerId)
                .Distinct()
                .Count();
            days.Add(new EmployeeParticipationDayView(day, employeesServed));
        }

        return new EmployeeParticipationWeekView(eligibleEmployeeCount, days);
    }
}
