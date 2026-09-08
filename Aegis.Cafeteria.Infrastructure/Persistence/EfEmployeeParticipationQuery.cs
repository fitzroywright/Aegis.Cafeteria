namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
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
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);

        HashSet<int> eligibleCustomerIds = await db.Customers
            .AsNoTracking()
            .Where(customer => customer.IsEligibleForCredit)
            .Select(customer => customer.Id)
            .ToHashSetAsync(cancellationToken);

        List<OrderRow> completedOrders = await db.Orders
            .AsNoTracking()
            .Where(order =>
                order.Status == (int)OrderStatus.Completed
                && order.CompletedAt.HasValue)
            .ToListAsync(cancellationToken);

        List<EmployeeParticipationDayView> days = [];
        for (int offset = 0; offset < 5; offset++)
        {
            DateOnly day = monday.AddDays(offset);
            int employeesServed = completedOrders
                .Where(order =>
                    eligibleCustomerIds.Contains(order.CustomerId)
                    && DateOnly.FromDateTime(order.CompletedAt!.Value.LocalDateTime) == day)
                .Select(order => order.CustomerId)
                .Distinct()
                .Count();
            days.Add(new EmployeeParticipationDayView(day, employeesServed));
        }

        return new EmployeeParticipationWeekView(eligibleCustomerIds.Count, days);
    }
}
