namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfCustomerAccountQuery(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    MonthlyCreditAllocationService allocationService,
    IFfpPublicationStatusProvider publicationStatusProvider) : ICustomerAccountQuery
{
    public async Task<CustomerAccountView?> GetAsync(int customerId, int year, int month, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        CustomerRow? customerRow;
        AccountPeriodRow? periodRow;
        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            customerRow = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
            if (customerRow is null) return null;
            periodRow = await db.AccountPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId && x.Year == year && x.Month == month, cancellationToken);
        }

        var customer = DomainReadMapper.ToDomain(customerRow);
        var allocation = await allocationService.EnsureAsync(customer, year, month, cancellationToken);
        var period = new AccountPeriod(year, month, allocation.EligibleWorkingDays);

        OrderRow[] orderRows;
        AccountEventRow[] events;
        await using (var db = await factory.CreateDbContextAsync(cancellationToken))
        {
            orderRows = await db.Orders.AsNoTracking().AsSplitQuery()
                .Include(x => x.Items)
                .Include(x => x.Payments)
                .Include(x => x.Refunds).ThenInclude(x => x.Items)
                .Include(x => x.Refunds).ThenInclude(x => x.Allocations)
                .Where(x => x.CustomerId == customerId && x.AccountYear == year && x.AccountMonth == month)
                .ToArrayAsync(cancellationToken);

            events = (await db.AccountEvents.AsNoTracking()
                .Where(x => x.CustomerId == customerId && x.AccountYear == year && x.AccountMonth == month)
                .ToArrayAsync(cancellationToken))
                .OrderBy(x => x.OccurredAt)
                .ThenBy(x => x.Id)
                .ToArray();
        }

        var orders = orderRows.Select(DomainReadMapper.ToDomain).ToArray();
        var approvals = Deserialize<AdditionalCreditApproval>(events, "AdditionalCreditApproval");
        var payments = Deserialize<CustomerPayment>(events, "CustomerPayment");
        var carriedDebt = Deserialize<CarriedForwardDebt>(events, "CarriedForwardDebt");
        var settlements = Deserialize<PriorPeriodOutstandingSettlement>(events, "PriorPeriodOutstandingSettlement");
        var stipends = Deserialize<Stipend>(events, "Stipend");
        var priorRefundCredits = Deserialize<PriorPeriodRefundCredit>(events, "PriorPeriodRefundCredit");

        CustomerFinancialStatusView financial;
        if (!customer.IsEligibleForCredit)
        {
            // Ineligible employees have no cafeteria-credit account. Keep cash transaction history
            // available, but do not calculate credit, subsidy, liability or externally supplied prior-period balances.
            financial = NoCreditFinancialStatus();
        }
        else
        {
            var snapshot = new CustomerAccountCalculator().Calculate(
                customer: customer,
                period: period,
                allocation: allocation,
                orders: orders,
                additionalCreditApprovals: approvals,
                payments: payments,
                carriedForwardDebts: carriedDebt,
                priorPeriodSettlements: settlements,
                stipends: stipends,
                priorPeriodRefundCredits: priorRefundCredits);

            financial = new CustomerFinancialStatusView(
                CreditLimit: snapshot.CreditLimit,
                SubsidyLimit: snapshot.SubsidyLimit,
                SubsidyUsed: snapshot.CompanyContributionUsed,
                SubsidyAmountRemaining: snapshot.SubsidyAmountRemaining,
                NonSubsidyAmountRemaining: snapshot.NonSubsidyAmountRemaining,
                TotalAvailableToSpend: snapshot.TotalAvailableToSpend,
                CarriedForwardDebt: snapshot.CarriedForwardDebt,
                GrossCustomerLiability: snapshot.GrossCustomerLiability,
                TotalOrderSales: snapshot.TotalOrderSales,
                CafeteriaAccountSpending: snapshot.CafeteriaAccountTendered - snapshot.CafeteriaAccountRefunded,
                CashSpending: snapshot.CashTendered - snapshot.CashRefunded,
                AdditionalCreditApproved: snapshot.AdditionalCreditApproved,
                AdditionalCreditUsed: snapshot.AdditionalCreditUsed,
                AdditionalCreditRemaining: snapshot.UnusedAdditionalCredit,
                StipendCashPaid: snapshot.StipendCashPaid,
                StipendCardReduction: snapshot.StipendCardReduction,
                PriorPeriodSettlements: snapshot.PriorPeriodOutstandingSettlements,
                PriorPeriodRefundCredits: snapshot.PriorPeriodRefundCredits,
                CustomerPayments: snapshot.CustomerPayments,
                CustomerResponsibility: snapshot.CustomerResponsibilityForCurrentCharges,
                EmployeeAmountDue: snapshot.EmployeeAmountDue,
                TotalRefunds: snapshot.TotalRefunds);
        }

        var publication = await publicationStatusProvider.GetAsync(cancellationToken);
        var transactions = BuildTransactions(orderRows, events, year, month);

        return new CustomerAccountView(
            new CustomerIdentityView(
                customer.Id,
                customer.Name,
                customer.Photo,
                customer.IsEligibleForCredit),
            new AccountPeriodView(year, month, allocation.EligibleWorkingDays, allocation.EmployeeFundedAllocation, allocation.SubsidyAllocation, periodRow?.IsClosed ?? false),
            financial,
            transactions,
            publication,
            BuildFundingStatus(customer.IsEligibleForCredit, allocation));
    }

    public async Task<IReadOnlyList<CustomerMonthlyTransactionView>> GetTransactionsAsync(
        int customerId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (year < 1) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var orderRows = await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Refunds).ThenInclude(x => x.Items)
            .Include(x => x.Refunds).ThenInclude(x => x.Allocations)
            .Where(x => x.CustomerId == customerId && x.AccountYear == year && x.AccountMonth == month)
            .ToArrayAsync(cancellationToken);

        var events = (await db.AccountEvents.AsNoTracking()
            .Where(x => x.CustomerId == customerId && x.AccountYear == year && x.AccountMonth == month)
            .ToArrayAsync(cancellationToken))
            .OrderBy(x => x.OccurredAt)
            .ThenBy(x => x.Id)
            .ToArray();

        return BuildTransactions(orderRows, events, year, month);
    }


    private static CustomerFinancialStatusView NoCreditFinancialStatus() => new(
        CreditLimit: 0m,
        SubsidyLimit: 0m,
        SubsidyUsed: 0m,
        SubsidyAmountRemaining: 0m,
        NonSubsidyAmountRemaining: 0m,
        TotalAvailableToSpend: 0m,
        CarriedForwardDebt: 0m,
        GrossCustomerLiability: 0m,
        TotalOrderSales: 0m,
        CafeteriaAccountSpending: 0m,
        CashSpending: 0m,
        AdditionalCreditApproved: 0m,
        AdditionalCreditUsed: 0m,
        AdditionalCreditRemaining: 0m,
        StipendCashPaid: 0m,
        StipendCardReduction: 0m,
        PriorPeriodSettlements: 0m,
        PriorPeriodRefundCredits: 0m,
        CustomerPayments: 0m,
        CustomerResponsibility: 0m,
        EmployeeAmountDue: 0m,
        TotalRefunds: 0m);

    private static CustomerFundingStatusView BuildFundingStatus
    (
          bool eligibleForCredit
        , MonthlyCreditAllocation allocation
    )
    {
        if (!eligibleForCredit)
        {
            return new CustomerFundingStatusView(false, false, null, "Not eligible for cafeteria credit");
        }

        return new CustomerFundingStatusView
        (
              true
            , true
            , allocation.CalculatedAt
            , $"Monthly Cafeteria funding loaded: credit rate {allocation.CreditLimitPrice:N2}; subsidy rate {allocation.SubsidyCost:N2}"
        );
    }

    private static T[] Deserialize<T>(IEnumerable<AccountEventRow> events, string type)
        => events.Where(x => x.EventType == type).Select(x => PersistencePayloadSerializer.Deserialize<T>(x.PayloadJson)).ToArray();

    private static IReadOnlyList<CustomerMonthlyTransactionView> BuildTransactions(IEnumerable<OrderRow> orders, IEnumerable<AccountEventRow> events, int year, int month)
    {
        var items = new List<CustomerMonthlyTransactionView>();
        foreach (var order in orders.Where(x => x.Status == (int)OrderStatus.Completed && x.CompletedAt.HasValue))
        {
            var description = order.Items.Count == 0 ? "Cafeteria purchase" : string.Join(", ", order.Items.Select(x => x.Quantity == 1 ? x.ProductName : $"{x.Quantity} x {x.ProductName}"));
            items.Add(new(order.CompletedAt!.Value,"Purchase",description,order.Items.Sum(x=>x.UnitPrice*x.Quantity),order.Payments.Where(x=>x.Method==(int)PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount),order.Payments.Where(x=>x.Method==(int)PaymentMethod.Cash).Sum(x=>x.Amount),order.Id));
            foreach (var refund in order.Refunds.Where(x => x.RefundedAt.Year == year && x.RefundedAt.Month == month))
                items.Add(new(refund.RefundedAt,"Refund",refund.Reason,-refund.Items.Sum(x=>x.UnitPrice*x.Quantity),-refund.Allocations.Where(x=>x.Method==(int)PaymentMethod.CafeteriaAccount).Sum(x=>x.Amount),-refund.Allocations.Where(x=>x.Method==(int)PaymentMethod.Cash).Sum(x=>x.Amount),refund.Id));
        }
        foreach (var e in events)
        {
            var entry = e.EventType switch
            {
                "AdditionalCreditApproval" => BuildAdditionalCredit(e),
                "CustomerPayment" => BuildCustomerPayment(e),
                "CarriedForwardDebt" => BuildCarriedDebt(e),
                "PriorPeriodOutstandingSettlement" => BuildSettlement(e),
                "PriorPeriodRefundCredit" => BuildPriorRefundCredit(e),
                "Stipend" => BuildStipend(e),
                _ => new CustomerMonthlyTransactionView(e.OccurredAt,e.EventType,e.EventType,e.Amount,0m,0m,e.Id)
            };
            items.Add(entry);
        }
        return items.OrderByDescending(x=>x.OccurredAt).ThenByDescending(x=>x.ReferenceId).ToArray();
    }

    private static CustomerMonthlyTransactionView BuildAdditionalCredit(AccountEventRow e) { var x=PersistencePayloadSerializer.Deserialize<AdditionalCreditApproval>(e.PayloadJson); return new(e.OccurredAt,"Additional Credit",string.IsNullOrWhiteSpace(x.Reason)?"Manager-approved additional credit":x.Reason!,x.Amount,0m,0m,x.Id); }
    private static CustomerMonthlyTransactionView BuildCustomerPayment(AccountEventRow e) { var x=PersistencePayloadSerializer.Deserialize<CustomerPayment>(e.PayloadJson); var detail = $"Customer payment - {x.PaymentMethod}" + (string.IsNullOrWhiteSpace(x.Reference) ? string.Empty : $" - {x.Reference}") + $" - received by {x.ReceivedByUserId}"; return new(e.OccurredAt,"Payment",detail,-x.Amount,0m,0m,x.Id); }
    private static CustomerMonthlyTransactionView BuildCarriedDebt(AccountEventRow e) { var x=PersistencePayloadSerializer.Deserialize<CarriedForwardDebt>(e.PayloadJson); return new(e.OccurredAt,"Debt Brought Forward",$"Unpaid debt from {x.SourceYear:D4}-{x.SourceMonth:D2}",x.Amount,0m,0m,x.Id); }
    private static CustomerMonthlyTransactionView BuildSettlement(AccountEventRow e) { var x=PersistencePayloadSerializer.Deserialize<PriorPeriodOutstandingSettlement>(e.PayloadJson); return new(e.OccurredAt,"Prior Period Correction",x.Reason,x.Amount,0m,0m,x.Id); }
    private static CustomerMonthlyTransactionView BuildPriorRefundCredit(AccountEventRow e) { var x=PersistencePayloadSerializer.Deserialize<PriorPeriodRefundCredit>(e.PayloadJson); return new(e.OccurredAt,"Prior Period Refund Credit",x.Reason,-x.CafeteriaAccountAmount,-x.CafeteriaAccountAmount,0m,x.Id); }
    private static CustomerMonthlyTransactionView BuildStipend(AccountEventRow e) { var x=PersistencePayloadSerializer.Deserialize<Stipend>(e.PayloadJson); return new(e.OccurredAt,"Off-site Stipend",$"{x.PaymentMode} - cash {x.CashAmount:C}; card reduction {x.CardReduction:C}",x.CardReduction,x.CardReduction,0m,x.Id); }
}
