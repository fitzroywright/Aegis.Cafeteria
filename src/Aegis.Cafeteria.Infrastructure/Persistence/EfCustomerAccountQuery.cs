namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Domain.Orders;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
        await using (LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken))
        {
            customerRow = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == customerId, cancellationToken);
            if (customerRow is null) return null;
            periodRow = await db.AccountPeriods.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId && x.Year == year && x.Month == month, cancellationToken);
        }

        Customer customer = DomainReadMapper.ToDomain(customerRow);
        MonthlyCreditAllocation allocation = await allocationService.EnsureAsync(customer, year, month, cancellationToken);
        AccountPeriod period = new(year, month, allocation.EligibleWorkingDays);

        OrderRow[] orderRows;
        AccountEventRow[] events;
        await using (LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken))
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

        Order[] orders = orderRows.Select(DomainReadMapper.ToDomain).ToArray();
        AdditionalCreditApproval[] approvals = Deserialize<AdditionalCreditApproval>(events, "AdditionalCreditApproval");
        CustomerPayment[] payments = Deserialize<CustomerPayment>(events, "CustomerPayment");
        CarriedForwardDebt[] carriedDebt = Deserialize<CarriedForwardDebt>(events, "CarriedForwardDebt");
        PriorPeriodOutstandingSettlement[] settlements = Deserialize<PriorPeriodOutstandingSettlement>(events, "PriorPeriodOutstandingSettlement");
        Stipend[] stipends = Deserialize<Stipend>(events, "Stipend");
        PriorPeriodRefundCredit[] priorRefundCredits = Deserialize<PriorPeriodRefundCredit>(events, "PriorPeriodRefundCredit");

        CustomerFinancialStatusView financial;
        if (!customer.IsEligibleForCredit)
        {
            financial = NoCreditFinancialStatus();
        }
        else
        {
            CustomerAccountSnapshot snapshot = new CustomerAccountCalculator().Calculate(
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

            decimal payrollDeduction = Math.Max(0m, allocation.EmployeeFundedAllocation - snapshot.NonSubsidyAmountRemaining);
            decimal outstandingBalance = Math.Max(0m, snapshot.EmployeeAmountDue - payrollDeduction);

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
                EmployeeAmountDue: outstandingBalance,
                TotalRefunds: snapshot.TotalRefunds,
                CurrentCreditSaleDebt: snapshot.CurrentCreditSaleDebt);
        }

        FfpPublicationStatus publication = await publicationStatusProvider.GetAsync(cancellationToken);
        IReadOnlyList<CustomerMonthlyTransactionView> transactions = BuildTransactions(orderRows, events, year, month);

        return new CustomerAccountView(
            new CustomerIdentityView(customer.Id, customer.Name, customer.Photo, customer.IsEligibleForCredit, false, customerRow.EmailAddress),
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

        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        OrderRow[] orderRows = await db.Orders.AsNoTracking().AsSplitQuery()
            .Include(x => x.Items)
            .Include(x => x.Payments)
            .Include(x => x.Refunds).ThenInclude(x => x.Items)
            .Include(x => x.Refunds).ThenInclude(x => x.Allocations)
            .Where(x => x.CustomerId == customerId && x.AccountYear == year && x.AccountMonth == month)
            .ToArrayAsync(cancellationToken);

        AccountEventRow[] events = (await db.AccountEvents.AsNoTracking()
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
        TotalRefunds: 0m,
        CurrentCreditSaleDebt: 0m);

    private static CustomerFundingStatusView BuildFundingStatus(bool eligibleForCredit, MonthlyCreditAllocation allocation)
    {
        if (!eligibleForCredit)
        {
            return new CustomerFundingStatusView(false, false, null, "Not eligible for cafeteria credit");
        }

        return new CustomerFundingStatusView(
            true,
            true,
            allocation.CalculatedAt,
            $"Monthly Cafeteria funding loaded: credit rate {allocation.CreditLimitPrice:N2}; subsidy rate {allocation.SubsidyCost:N2}");
    }

    private static T[] Deserialize<T>(IEnumerable<AccountEventRow> events, string type)
        => events.Where(x => x.EventType == type).Select(x => PersistencePayloadSerializer.Deserialize<T>(x.PayloadJson)).ToArray();

    private static IReadOnlyList<CustomerMonthlyTransactionView> BuildTransactions(IEnumerable<OrderRow> orders, IEnumerable<AccountEventRow> events, int year, int month)
    {
        List<CustomerMonthlyTransactionView> items = [];
        foreach (OrderRow order in orders.Where(x => x.Status == (int)OrderStatus.Completed && x.CompletedAt.HasValue))
        {
            string description = order.Items.Count == 0 ? "Cafeteria purchase" : string.Join(", ", order.Items.Select(x => x.Quantity == 1 ? x.ProductName : $"{x.Quantity} x {x.ProductName}"));
            decimal creditAmount = order.Payments.Where(x => x.Method == (int)PaymentMethod.Credit).Sum(x => x.Amount);
            if (creditAmount > 0m)
                description += $" • Employee Credit {creditAmount:C2}";

            items.Add(new(order.CompletedAt!.Value, "Purchase", description, order.Items.Sum(x => x.UnitPrice * x.Quantity), order.Payments.Where(x => x.Method == (int)PaymentMethod.CafeteriaAccount).Sum(x => x.Amount), order.Payments.Where(x => x.Method == (int)PaymentMethod.Cash).Sum(x => x.Amount), order.Id));
            foreach (OrderRefundRow refund in order.Refunds.Where(x => x.RefundedAt.Year == year && x.RefundedAt.Month == month))
            {
                decimal creditRefund = refund.Allocations.Where(x => x.Method == (int)PaymentMethod.Credit).Sum(x => x.Amount);
                string refundDescription = creditRefund > 0m
                    ? $"{refund.Reason} • Employee Credit reduced {creditRefund:C2}"
                    : refund.Reason;
                items.Add(new(refund.RefundedAt, "Refund", refundDescription, -refund.Items.Sum(x => x.UnitPrice * x.Quantity), -refund.Allocations.Where(x => x.Method == (int)PaymentMethod.CafeteriaAccount).Sum(x => x.Amount), -refund.Allocations.Where(x => x.Method == (int)PaymentMethod.Cash).Sum(x => x.Amount), refund.Id));
            }
        }

        foreach (AccountEventRow accountEvent in events)
        {
            CustomerMonthlyTransactionView entry = accountEvent.EventType switch
            {
                "AdditionalCreditApproval" => BuildAdditionalCredit(accountEvent),
                "CustomerPayment" => BuildCustomerPayment(accountEvent),
                "CarriedForwardDebt" => BuildCarriedDebt(accountEvent),
                "PriorPeriodOutstandingSettlement" => BuildSettlement(accountEvent),
                "PriorPeriodRefundCredit" => BuildPriorRefundCredit(accountEvent),
                "Stipend" => BuildStipend(accountEvent),
                "ImportedFfpTransaction" => BuildImportedFfpTransaction(accountEvent),
                _ => new CustomerMonthlyTransactionView(accountEvent.OccurredAt, accountEvent.EventType, accountEvent.EventType, accountEvent.Amount, 0m, 0m, accountEvent.Id)
            };
            items.Add(entry);
        }

        return items.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.ReferenceId).ToArray();
    }

    private static CustomerMonthlyTransactionView BuildImportedFfpTransaction(AccountEventRow accountEvent)
    {
        ExternalFfpTransactionSnapshot transaction = JsonSerializer.Deserialize<ExternalFfpTransactionSnapshot>(accountEvent.PayloadJson)
            ?? throw new InvalidOperationException("Imported FFP transaction payload is invalid.");

        if (transaction.ScanType == 8)
        {
            string itemName = string.IsNullOrWhiteSpace(transaction.IdName) ? "Legacy cafeteria item" : transaction.IdName.Trim();
            return new CustomerMonthlyTransactionView(
                transaction.OccurredAt,
                "Purchase",
                $"{itemName} (Imported FFP - ID Card)",
                transaction.ScanValue,
                transaction.ScanValue,
                0m,
                accountEvent.Id);
        }

        if (transaction.ScanType == 3)
        {
            return new CustomerMonthlyTransactionView(transaction.OccurredAt, "Allocation", transaction.IdName, transaction.ScanValue, 0m, 0m, accountEvent.Id);
        }

        if (transaction.ScanType == 4)
        {
            return new CustomerMonthlyTransactionView(transaction.OccurredAt, "Stipend", transaction.IdName, transaction.ScanValue, 0m, 0m, accountEvent.Id);
        }

        return new CustomerMonthlyTransactionView(transaction.OccurredAt, "Imported FFP", transaction.IdName, transaction.ScanValue, 0m, 0m, accountEvent.Id);
    }

    private static CustomerMonthlyTransactionView BuildAdditionalCredit(AccountEventRow e) { AdditionalCreditApproval x = PersistencePayloadSerializer.Deserialize<AdditionalCreditApproval>(e.PayloadJson); return new(e.OccurredAt, "Additional Credit", string.IsNullOrWhiteSpace(x.Reason) ? "Manager-approved additional credit" : x.Reason!, x.Amount, 0m, 0m, x.Id); }
    private static CustomerMonthlyTransactionView BuildCustomerPayment(AccountEventRow e)
    {
        CustomerPayment x = PersistencePayloadSerializer.Deserialize<CustomerPayment>(e.PayloadJson);
        string detail = $"Customer payment - {x.PaymentMethod}"
            + (string.IsNullOrWhiteSpace(x.Reference) ? string.Empty : $" - {x.Reference}")
            + $" - received by {x.ReceivedByUserId}";
        decimal amountDueBeforePayment = x.OutstandingBalanceBefore ?? x.Amount;
        decimal balanceAfterPayment = x.OutstandingBalanceAfter ?? Math.Max(0m, amountDueBeforePayment - x.Amount);
        return new CustomerMonthlyTransactionView(
            e.OccurredAt,
            "Payment",
            detail,
            amountDueBeforePayment,
            0m,
            x.Amount,
            x.Id,
            amountDueBeforePayment,
            balanceAfterPayment);
    }
    private static CustomerMonthlyTransactionView BuildCarriedDebt(AccountEventRow e) { CarriedForwardDebt x = PersistencePayloadSerializer.Deserialize<CarriedForwardDebt>(e.PayloadJson); return new(e.OccurredAt, "Debt Brought Forward", $"Unpaid debt from {x.SourceYear:D4}-{x.SourceMonth:D2}", x.Amount, 0m, 0m, x.Id); }
    private static CustomerMonthlyTransactionView BuildSettlement(AccountEventRow e) { PriorPeriodOutstandingSettlement x = PersistencePayloadSerializer.Deserialize<PriorPeriodOutstandingSettlement>(e.PayloadJson); return new(e.OccurredAt, "Prior Period Correction", x.Reason, x.Amount, 0m, 0m, x.Id); }
    private static CustomerMonthlyTransactionView BuildPriorRefundCredit(AccountEventRow e) { PriorPeriodRefundCredit x = PersistencePayloadSerializer.Deserialize<PriorPeriodRefundCredit>(e.PayloadJson); return new(e.OccurredAt, "Prior Period Refund Credit", x.Reason, -x.CafeteriaAccountAmount, -x.CafeteriaAccountAmount, 0m, x.Id); }
    private static CustomerMonthlyTransactionView BuildStipend(AccountEventRow e) { Stipend x = PersistencePayloadSerializer.Deserialize<Stipend>(e.PayloadJson); return new(e.OccurredAt, "Off-site Stipend", $"{x.PaymentMode} - cash {x.CashAmount:C}; card reduction {x.CardReduction:C}", x.CardReduction, x.CardReduction, 0m, x.Id); }
}
