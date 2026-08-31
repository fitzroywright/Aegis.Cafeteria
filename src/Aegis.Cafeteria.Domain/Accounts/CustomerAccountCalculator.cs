namespace Aegis.Cafeteria.Domain.Accounts;

using Aegis.Cafeteria.Domain.Customers;
using Aegis.Cafeteria.Domain.Orders;

public sealed class CustomerAccountCalculator
{
    public CustomerAccountSnapshot Calculate(
        Customer customer,
        AccountPeriod period,
        MonthlyCreditAllocation allocation,
        IEnumerable<Order> orders,
        IEnumerable<AdditionalCreditApproval> additionalCreditApprovals,
        IEnumerable<CustomerPayment> payments,
        IEnumerable<CarriedForwardDebt> carriedForwardDebts,
        IEnumerable<PriorPeriodOutstandingSettlement> priorPeriodSettlements,
        IEnumerable<Stipend> stipends,
        IEnumerable<PriorPeriodRefundCredit>? priorPeriodRefundCredits = null)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(period);
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(additionalCreditApprovals);
        ArgumentNullException.ThrowIfNull(payments);
        ArgumentNullException.ThrowIfNull(carriedForwardDebts);
        ArgumentNullException.ThrowIfNull(priorPeriodSettlements);
        ArgumentNullException.ThrowIfNull(stipends);

        if (allocation.CustomerId != customer.Id || allocation.Year != period.Year || allocation.Month != period.Month)
            throw new InvalidOperationException("The monthly allocation does not belong to the supplied customer and account period.");

        var baseMonthlyCredit = allocation.TotalCreditLimit;
        var subsidyLimit = allocation.SubsidyAllocation;

        var carriedDebt = carriedForwardDebts
            .Where(x => x.CustomerId == customer.Id && x.AppliedYear == period.Year && x.AppliedMonth == period.Month)
            .Sum(x => x.Amount);

        // Retained for explicit discovered/corrected prior-period amounts. Normal unpaid debt is represented by CarriedForwardDebt.
        var settlementAmount = priorPeriodSettlements
            .Where(x => x.CustomerId == customer.Id && x.SettlementYear == period.Year && x.SettlementMonth == period.Month)
            .Sum(x => x.Amount);

        var periodStipends = stipends
            .Where(x => x.CustomerId == customer.Id && period.Contains(x.ProcessedAt))
            .ToArray();
        var stipendCashPaid = periodStipends.Sum(x => x.CashAmount);
        var stipendCardReduction = periodStipends.Sum(x => x.CardReduction);

        if (stipendCardReduction > subsidyLimit)
            throw new InvalidOperationException("Stipend card reductions cannot exceed the period subsidy allocation.");

        var baseCreditAvailableForCurrentPurchases = Math.Max(0m, baseMonthlyCredit - settlementAmount - stipendCardReduction - carriedDebt);
        var remainingCompanyContribution = Math.Max(0m, subsidyLimit - stipendCardReduction);

        var periodOrders = orders
            .Where(order => order.CustomerId == customer.Id && order.Status == OrderStatus.Completed && order.CompletedAt.HasValue && period.Contains(order.CompletedAt.Value))
            .ToArray();

        var totalOrderSales = periodOrders.Sum(order => order.Total);
        var cafeteriaAccountTendered = periodOrders.Sum(order => order.CafeteriaAccountPaid);
        var cashTendered = periodOrders.Sum(order => order.CashPaid);

        var periodRefunds = periodOrders.SelectMany(order => order.Refunds).Where(refund => period.Contains(refund.RefundedAt)).ToArray();
        var totalRefunds = periodRefunds.Sum(x => x.Total);
        var cafeteriaAccountRefunded = periodRefunds.Sum(x => x.CafeteriaAccountAmount);
        var cashRefunded = periodRefunds.Sum(x => x.CashAmount);
        var totalCharges = Math.Max(0m, cafeteriaAccountTendered - cafeteriaAccountRefunded);

        var additionalCreditApproved = additionalCreditApprovals
            .Where(x => x.CustomerId == customer.Id && period.Contains(x.ApprovedAt))
            .Sum(x => x.Amount);
        if (additionalCreditApproved > customer.AdditionalCreditLimit)
            throw new InvalidOperationException("Approved additional credit exceeds the customer's additional credit limit.");

        var priorRefundCredits = (priorPeriodRefundCredits ?? [])
            .Where(x => x.CustomerId == customer.Id && x.AppliedYear == period.Year && x.AppliedMonth == period.Month)
            .Sum(x => x.CafeteriaAccountAmount);

        var subsidyEligibleCharges = Math.Min(totalCharges, baseCreditAvailableForCurrentPurchases);
        var companyResponsibility = Math.Min(subsidyEligibleCharges, remainingCompanyContribution);
        var companyContributionUsed = stipendCardReduction + companyResponsibility;
        remainingCompanyContribution = Math.Max(0m, subsidyLimit - companyContributionUsed);

        var customerResponsibilityForCurrentCharges = Math.Max(0m, totalCharges - companyResponsibility);
        var grossCustomerLiability = carriedDebt + settlementAmount + customerResponsibilityForCurrentCharges;
        var customerPayments = payments
            .Where(x => x.CustomerId == customer.Id && period.Contains(x.ReceivedAt))
            .Sum(x => x.Amount);
        if (customerPayments > grossCustomerLiability)
            throw new InvalidOperationException("Customer payments cannot exceed the customer's outstanding responsibility.");
        var employeeAmountDue = Math.Max(0m, grossCustomerLiability - customerPayments);

        // Positive unused balances are period-scoped and expire at close. Carried debt reduces current capacity,
        // but a customer payment clears liability and therefore re-opens the corresponding revolving credit.
        // Payments never restore availability above the approved period spending capacity.
        var totalSpendingCapacity = baseMonthlyCredit + additionalCreditApproved + priorRefundCredits;
        var totalAllocationUsage = carriedDebt + settlementAmount + stipendCardReduction + totalCharges;
        var netAllocationUsage = Math.Max(0m, totalAllocationUsage - customerPayments);
        var availableCredit = Math.Clamp(totalSpendingCapacity - netAllocationUsage, 0m, totalSpendingCapacity);

        var usageBeyondBase = Math.Max(0m, netAllocationUsage - baseMonthlyCredit);
        var additionalCreditUsed = Math.Min(additionalCreditApproved, usageBeyondBase);
        var unusedAdditionalCredit = Math.Max(0m, additionalCreditApproved - additionalCreditUsed);

        return new CustomerAccountSnapshot(
            BaseMonthlyCredit: baseMonthlyCredit,
            PriorPeriodOutstandingSettlements: settlementAmount,
            StipendCashPaid: stipendCashPaid,
            StipendCardReduction: stipendCardReduction,
            BaseCreditAvailableForCurrentPurchases: baseCreditAvailableForCurrentPurchases,
            AdditionalCreditApproved: additionalCreditApproved,
            TotalSpendingCapacity: totalSpendingCapacity,
            TotalCharges: totalCharges,
            CompanyResponsibility: companyResponsibility,
            CompanyContributionUsed: companyContributionUsed,
            RemainingCompanyContribution: remainingCompanyContribution,
            CustomerResponsibilityForCurrentCharges: customerResponsibilityForCurrentCharges,
            CustomerPayments: customerPayments,
            EmployeeAmountDue: employeeAmountDue,
            AvailableCredit: availableCredit,
            AdditionalCreditUsed: additionalCreditUsed,
            UnusedAdditionalCredit: unusedAdditionalCredit,
            TotalOrderSales: totalOrderSales,
            CafeteriaAccountTendered: cafeteriaAccountTendered,
            CashTendered: cashTendered,
            TotalRefunds: totalRefunds,
            CafeteriaAccountRefunded: cafeteriaAccountRefunded,
            CashRefunded: cashRefunded,
            PriorPeriodRefundCredits: priorRefundCredits,
            MonthlyEmployeeFundedAllocation: allocation.EmployeeFundedAllocation,
            MonthlySubsidyAllocation: allocation.SubsidyAllocation,
            CarriedForwardDebt: carriedDebt,
            GrossCustomerLiability: grossCustomerLiability);
    }

}
