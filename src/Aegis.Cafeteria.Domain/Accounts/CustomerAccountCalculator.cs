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

        decimal baseMonthlyCredit = allocation.TotalCreditLimit;
        decimal subsidyLimit = allocation.SubsidyAllocation;

        decimal carriedDebt = carriedForwardDebts
            .Where(x => x.CustomerId == customer.Id && x.AppliedYear == period.Year && x.AppliedMonth == period.Month)
            .Sum(x => x.Amount);

        // Retained for explicit discovered/corrected prior-period amounts. Normal unpaid debt is represented by CarriedForwardDebt.
        decimal settlementAmount = priorPeriodSettlements
            .Where(x => x.CustomerId == customer.Id && x.SettlementYear == period.Year && x.SettlementMonth == period.Month)
            .Sum(x => x.Amount);

        Stipend[] periodStipends = stipends
            .Where(x => x.CustomerId == customer.Id && period.Contains(x.ProcessedAt))
            .ToArray();
        decimal stipendCashPaid = periodStipends.Sum(x => x.CashAmount);
        decimal stipendCardReduction = periodStipends.Sum(x => x.CardReduction);

        if (stipendCardReduction > subsidyLimit)
            throw new InvalidOperationException("Stipend card reductions cannot exceed the period subsidy allocation.");

        decimal baseCreditAvailableForCurrentPurchases = Math.Max(0m, baseMonthlyCredit - settlementAmount - stipendCardReduction - carriedDebt);
        decimal remainingCompanyContribution = Math.Max(0m, subsidyLimit - stipendCardReduction);

        Order[] periodOrders = orders
            .Where(order => order.CustomerId == customer.Id && order.Status == OrderStatus.Completed && order.CompletedAt.HasValue && period.Contains(order.CompletedAt.Value))
            .ToArray();

        decimal totalOrderSales = periodOrders.Sum(order => order.Total);
        decimal cafeteriaAccountTendered = periodOrders.Sum(order => order.CafeteriaAccountPaid);
        decimal cashTendered = periodOrders.Sum(order => order.CashPaid);
        decimal creditTendered = periodOrders.Sum(order => order.CreditPaid);

        OrderRefund[] periodRefunds = periodOrders.SelectMany(order => order.Refunds).Where(refund => period.Contains(refund.RefundedAt)).ToArray();
        decimal totalRefunds = periodRefunds.Sum(x => x.Total);
        decimal cafeteriaAccountRefunded = periodRefunds.Sum(x => x.CafeteriaAccountAmount);
        decimal cashRefunded = periodRefunds.Sum(x => x.CashAmount);
        decimal creditRefunded = periodRefunds.Sum(x => x.CreditAmount);
        decimal totalCharges = Math.Max(0m, cafeteriaAccountTendered - cafeteriaAccountRefunded);
        decimal currentCreditSaleDebt = Math.Max(0m, creditTendered - creditRefunded);

        decimal additionalCreditApproved = additionalCreditApprovals
            .Where(x => x.CustomerId == customer.Id && period.Contains(x.ApprovedAt))
            .Sum(x => x.Amount);
        if (additionalCreditApproved > customer.AdditionalCreditLimit)
            throw new InvalidOperationException("Approved additional credit exceeds the customer's additional credit limit.");

        decimal priorRefundCredits = (priorPeriodRefundCredits ?? [])
            .Where(x => x.CustomerId == customer.Id && x.AppliedYear == period.Year && x.AppliedMonth == period.Month)
            .Sum(x => x.CafeteriaAccountAmount);

        decimal subsidyEligibleCharges = Math.Min(totalCharges, baseCreditAvailableForCurrentPurchases);
        decimal companyResponsibility = Math.Min(subsidyEligibleCharges, remainingCompanyContribution);
        decimal companyContributionUsed = stipendCardReduction + companyResponsibility;
        remainingCompanyContribution = Math.Max(0m, subsidyLimit - companyContributionUsed);

        decimal customerResponsibilityForCurrentCharges = Math.Max(0m, totalCharges - companyResponsibility);
        decimal grossCustomerLiability = carriedDebt + settlementAmount + customerResponsibilityForCurrentCharges + currentCreditSaleDebt;
        decimal customerPayments = payments
            .Where(x => x.CustomerId == customer.Id && period.Contains(x.ReceivedAt))
            .Sum(x => x.Amount);
        if (customerPayments > grossCustomerLiability)
            throw new InvalidOperationException("Customer payments cannot exceed the customer's outstanding responsibility.");

        decimal employeeAmountDue = Math.Max(0m, grossCustomerLiability - customerPayments);

        // Credit-sale debt is deliberately outside the employee-card allocation. Payments clear that debt first,
        // so paying off an over-limit credit sale cannot create fresh cafeteria-card spending capacity.
        decimal paymentsAppliedToCreditDebt = Math.Min(customerPayments, currentCreditSaleDebt);
        decimal paymentsAppliedToAllocationLiability = Math.Max(0m, customerPayments - paymentsAppliedToCreditDebt);

        // Positive unused balances are period-scoped and expire at close. Carried debt reduces current capacity.
        // Payments against allocation liability may re-open the corresponding revolving card capacity, but never
        // above the approved period spending capacity.
        decimal totalSpendingCapacity = baseMonthlyCredit + additionalCreditApproved + priorRefundCredits;
        decimal totalAllocationUsage = carriedDebt + settlementAmount + stipendCardReduction + totalCharges;
        decimal netAllocationUsage = Math.Max(0m, totalAllocationUsage - paymentsAppliedToAllocationLiability);
        decimal availableCredit = Math.Clamp(totalSpendingCapacity - netAllocationUsage, 0m, totalSpendingCapacity);

        decimal usageBeyondBase = Math.Max(0m, netAllocationUsage - baseMonthlyCredit);
        decimal additionalCreditUsed = Math.Min(additionalCreditApproved, usageBeyondBase);
        decimal unusedAdditionalCredit = Math.Max(0m, additionalCreditApproved - additionalCreditUsed);

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
            CreditTendered: creditTendered,
            TotalRefunds: totalRefunds,
            CafeteriaAccountRefunded: cafeteriaAccountRefunded,
            CashRefunded: cashRefunded,
            CreditRefunded: creditRefunded,
            PriorPeriodRefundCredits: priorRefundCredits,
            MonthlyEmployeeFundedAllocation: allocation.EmployeeFundedAllocation,
            MonthlySubsidyAllocation: allocation.SubsidyAllocation,
            CarriedForwardDebt: carriedDebt,
            CurrentCreditSaleDebt: currentCreditSaleDebt,
            GrossCustomerLiability: grossCustomerLiability);
    }
}
