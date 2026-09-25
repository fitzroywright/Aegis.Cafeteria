namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;

public sealed class StipendAccountSnapshotFactory
{
    private readonly ServiceEventStore events;
    private readonly IPayrollLedgerSource payrollLedger;

    public StipendAccountSnapshotFactory(
        ServiceEventStore events,
        IPayrollLedgerSource payrollLedger)
    {
        this.events = events;
        this.payrollLedger = payrollLedger;
    }

    public async Task<CustomerAccountSnapshot> BuildAsync(
        int customerId,
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, MonthlyAllocationServiceEvent> allocations =
            await this.events.GetCurrentMonthlyAllocationsAsync(
                year,
                month,
                cancellationToken);

        if (!allocations.TryGetValue(customerId, out MonthlyAllocationServiceEvent? allocationEvent))
        {
            throw new InvalidOperationException(
                $"Month {year:D4}-{month:D2} has not been opened for employee {customerId}.");
        }

        MonthlyCreditAllocation allocation = new(
            allocationEvent.CustomerId,
            allocationEvent.Year,
            allocationEvent.Month,
            allocationEvent.EligibleWorkingDays,
            allocationEvent.IsEligibleForCredit,
            allocationEvent.CreditLimitPrice,
            allocationEvent.SubsidyCost,
            allocationEvent.CalculatedAt);

        IReadOnlyDictionary<int, PayrollLedgerTotals> ledgerTotals =
            await this.payrollLedger.GetMonthlyTotalsAsync(
                year,
                month,
                cancellationToken);

        PayrollLedgerTotals ledger = ledgerTotals.TryGetValue(
            customerId,
            out PayrollLedgerTotals? existingLedger)
            ? existingLedger
            : PayrollLedgerTotals.Empty;

        decimal additionalCredit = await this.events.GetAdditionalCreditTotalAsync(
            customerId,
            year,
            month,
            cancellationToken);

        decimal netCafeteriaCharges = Math.Max(
            0m,
            ledger.Purchases - ledger.Refunds);

        decimal existingStipendReduction = ledger.StipendCardReduction;
        decimal baseCreditAfterStipends = Math.Max(
            0m,
            allocation.TotalCreditLimit - existingStipendReduction);

        decimal subsidyUsedByPurchases = Math.Min(
            netCafeteriaCharges,
            baseCreditAfterStipends);

        decimal companyContributionUsed = Math.Min(
            allocation.SubsidyAllocation,
            existingStipendReduction + subsidyUsedByPurchases);

        decimal remainingCompanyContribution = Math.Max(
            0m,
            allocation.SubsidyAllocation - companyContributionUsed);

        decimal employeeResponsibility = Math.Max(
            0m,
            netCafeteriaCharges
            - Math.Max(
                0m,
                companyContributionUsed - existingStipendReduction));

        decimal employeeAmountDue = Math.Max(
            0m,
            employeeResponsibility - ledger.CustomerPayments);

        decimal totalSpendingCapacity =
            allocation.TotalCreditLimit + additionalCredit;

        decimal availableCredit = Math.Max(
            0m,
            totalSpendingCapacity
            - netCafeteriaCharges
            - existingStipendReduction
            + ledger.CustomerPayments);

        decimal additionalCreditUsed = Math.Min(
            additionalCredit,
            Math.Max(
                0m,
                netCafeteriaCharges
                + existingStipendReduction
                - ledger.CustomerPayments
                - allocation.TotalCreditLimit));

        return new CustomerAccountSnapshot(
              BaseMonthlyCredit: allocation.TotalCreditLimit
            , PriorPeriodOutstandingSettlements: 0m
            , StipendCashPaid: 0m
            , StipendCardReduction: existingStipendReduction
            , BaseCreditAvailableForCurrentPurchases: Math.Min(
                allocation.TotalCreditLimit,
                availableCredit)
            , AdditionalCreditApproved: additionalCredit
            , TotalSpendingCapacity: totalSpendingCapacity
            , TotalCharges: netCafeteriaCharges
            , CompanyResponsibility: Math.Max(
                0m,
                companyContributionUsed - existingStipendReduction)
            , CompanyContributionUsed: companyContributionUsed
            , RemainingCompanyContribution: remainingCompanyContribution
            , CustomerResponsibilityForCurrentCharges: employeeResponsibility
            , CustomerPayments: ledger.CustomerPayments
            , EmployeeAmountDue: employeeAmountDue
            , AvailableCredit: availableCredit
            , AdditionalCreditUsed: additionalCreditUsed
            , UnusedAdditionalCredit: Math.Max(
                0m,
                additionalCredit - additionalCreditUsed)
            , TotalOrderSales: netCafeteriaCharges
            , CafeteriaAccountTendered: ledger.Purchases
            , TotalRefunds: ledger.Refunds
            , CafeteriaAccountRefunded: ledger.Refunds
            , MonthlyEmployeeFundedAllocation: allocation.EmployeeFundedAllocation
            , MonthlySubsidyAllocation: allocation.SubsidyAllocation);
    }
}
