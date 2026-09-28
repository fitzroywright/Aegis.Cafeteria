namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;
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

            await EnsureCurrentCreditAsync(db, row, cancellationToken);
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

    // Called inside LocalPersistenceStore's database transaction. The screen's balance
    // is only a quote; authoritative local ledger state is re-read before a new sale commits.
    private static async Task EnsureCurrentCreditAsync(LocalCafeteriaDbContext db, OrderRow row, CancellationToken ct)
    {
        decimal card = row.Payments.Where(payment => payment.Method == (int)PaymentMethod.CafeteriaAccount).Sum(payment => payment.Amount);
        decimal credit = row.Payments.Where(payment => payment.Method == (int)PaymentMethod.Credit).Sum(payment => payment.Amount);
        if (row.Status != (int)OrderStatus.Completed || (card == 0m && credit == 0m))
        {
            return;
        }
        CustomerRow customerRow = await db.Customers.AsNoTracking().SingleOrDefaultAsync(customer => customer.Id == row.CustomerId, ct)
            ?? throw new InvalidOperationException("Employee is no longer available. Re-select the employee.");
        if (!customerRow.IsActive || !customerRow.IsEligibleForCredit)
        {
            throw new InvalidOperationException("The employee is no longer eligible for credit. Cash payment is required.");
        }
        int year = row.AccountYear ?? throw new InvalidOperationException("Sale account year is missing.");
        int month = row.AccountMonth ?? throw new InvalidOperationException("Sale account month is missing.");
        MonthlyCreditAllocationRow allocationRow = await db.MonthlyCreditAllocations.AsNoTracking()
            .SingleOrDefaultAsync(allocation => allocation.CustomerId == row.CustomerId && allocation.Year == year && allocation.Month == month, ct)
            ?? throw new InvalidOperationException("Employee funding is not available. Refresh the account before checkout.");
        MonthlyCreditAllocation allocation = new(allocationRow.CustomerId, year, month, allocationRow.EligibleWorkingDays,
            allocationRow.IsEligibleForCredit, allocationRow.CreditLimitPrice, allocationRow.SubsidyCost, allocationRow.CalculatedAt);
        OrderRow[] orders = await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(order => order.Items).Include(order => order.Payments)
            .Include(order => order.Refunds).ThenInclude(refund => refund.Items)
            .Include(order => order.Refunds).ThenInclude(refund => refund.Allocations)
            .Where(order => order.CustomerId == row.CustomerId && order.AccountYear == year && order.AccountMonth == month)
            .ToArrayAsync(ct);
        AccountEventRow[] events = await db.AccountEvents.AsNoTracking()
            .Where(accountEvent => accountEvent.CustomerId == row.CustomerId && accountEvent.AccountYear == year && accountEvent.AccountMonth == month)
            .ToArrayAsync(ct);
        Customer customer = DomainReadMapper.ToDomain(customerRow);
        CustomerAccountSnapshot snapshot = new CustomerAccountCalculator().Calculate
        (
              customer
            , new AccountPeriod(year, month, allocation.EligibleWorkingDays)
            , allocation
            , orders.Select(DomainReadMapper.ToDomain)
            , ReadEvents<AdditionalCreditApproval>(events, "AdditionalCreditApproval")
            , ReadEvents<CustomerPayment>(events, "CustomerPayment")
            , ReadEvents<CarriedForwardDebt>(events, "CarriedForwardDebt")
            , ReadEvents<PriorPeriodOutstandingSettlement>(events, "PriorPeriodOutstandingSettlement")
            , ReadEvents<Stipend>(events, "Stipend")
            , ReadEvents<PriorPeriodRefundCredit>(events, "PriorPeriodRefundCredit")
        );
        if (snapshot.CreditShortfall > 0m)
        {
            throw new InvalidOperationException($"Credit changed: the account is over its limit by {snapshot.CreditShortfall:N2}. Cash payment is required.");
        }
        if (card > snapshot.TotalAvailableToSpend)
        {
            throw new InvalidOperationException($"Available credit changed to {snapshot.TotalAvailableToSpend:N2}. Review the payment amounts and retry.");
        }
    }

    private static T[] ReadEvents<T>(IEnumerable<AccountEventRow> events, string type)
        => events.Where(accountEvent => accountEvent.EventType == type)
            .Select(accountEvent => PersistencePayloadSerializer.Deserialize<T>(accountEvent.PayloadJson)).ToArray();

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
