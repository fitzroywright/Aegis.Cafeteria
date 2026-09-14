namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

internal sealed class FinancialPostingService
{
    public async Task SaveAccountEventAsync(
        LocalCafeteriaDbContext db,
        AccountEventRow row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(row);

        await EnsureOpenAsync(
            db,
            row.CustomerId,
            row.AccountYear,
            row.AccountMonth,
            row.EventType,
            cancellationToken);

        AccountEventRow? existing = await db.AccountEvents
            .SingleOrDefaultAsync(x => x.Id == row.Id, cancellationToken);

        if (existing is null)
        {
            db.AccountEvents.Add(row);
            return;
        }

        db.Entry(existing).CurrentValues.SetValues(row);
    }

    public async Task SaveOrderAsync(
        LocalCafeteriaDbContext db,
        OrderRow row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(row);

        OrderRow? existing = await db.Orders
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Refunds).ThenInclude(x => x.Items)
            .Include(x => x.Refunds).ThenInclude(x => x.Allocations)
            .SingleOrDefaultAsync(x => x.Id == row.Id, cancellationToken);

        if (existing is null)
        {
            if (row.AccountYear.HasValue && row.AccountMonth.HasValue)
            {
                await EnsureOpenAsync(
                    db,
                    row.CustomerId,
                    row.AccountYear.Value,
                    row.AccountMonth.Value,
                    "Sale",
                    cancellationToken);
            }

            db.Orders.Add(row);
            return;
        }

        // Existing completed orders may legitimately receive a current-period refund
        // that references a closed source period. RefundService is responsible for
        // creating the explicit PriorPeriodRefundCredit in the applied open period.
        db.Orders.Remove(existing);
        db.Orders.Add(row);
    }

    public async Task SaveMonthlyCreditAllocationAsync(
        LocalCafeteriaDbContext db,
        MonthlyCreditAllocationRow row,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(row);

        await EnsureOpenAsync(
            db,
            row.CustomerId,
            row.Year,
            row.Month,
            "MonthlyCreditAllocation",
            cancellationToken);

        MonthlyCreditAllocationRow? existing = await db.MonthlyCreditAllocations
            .SingleOrDefaultAsync(
                x => x.CustomerId == row.CustomerId &&
                     x.Year == row.Year &&
                     x.Month == row.Month,
                cancellationToken);

        if (existing is null)
        {
            db.MonthlyCreditAllocations.Add(row);
            return;
        }

        if (!Equivalent(existing, row))
        {
            throw new InvalidOperationException("A monthly credit allocation is immutable once calculated.");
        }
    }

    private static async Task EnsureOpenAsync(
        LocalCafeteriaDbContext db,
        int customerId,
        int year,
        int month,
        string postingType,
        CancellationToken cancellationToken)
    {
        bool isClosed = await db.AccountPeriods
            .AsNoTracking()
            .AnyAsync(
                x => x.CustomerId == customerId &&
                     x.Year == year &&
                     x.Month == month &&
                     x.IsClosed,
                cancellationToken);

        if (!isClosed)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Cannot post {postingType} to closed cafeteria period {year:D4}-{month:D2}. " +
            "Record an explicit later-period correction instead.");
    }

    private static bool Equivalent(
        MonthlyCreditAllocationRow first,
        MonthlyCreditAllocationRow second)
        => first.CustomerId == second.CustomerId &&
           first.Year == second.Year &&
           first.Month == second.Month &&
           first.EligibleWorkingDays == second.EligibleWorkingDays &&
           first.IsEligibleForCredit == second.IsEligibleForCredit &&
           first.CreditLimitPrice == second.CreditLimitPrice &&
           first.SubsidyCost == second.SubsidyCost &&
           first.TotalCreditLimit == second.TotalCreditLimit &&
           first.SubsidyAllocation == second.SubsidyAllocation;
}
