namespace Aegis.Cafeteria.Services;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Email;
using Aegis.Cafeteria.Services.Persistence;

public sealed class StipendAccountSnapshotFactory
{
    private readonly ServiceEventStore events;
    private readonly IExternalAccountingGateway accounting;
    private readonly IPayrollLedgerSource payrollLedger;

    public StipendAccountSnapshotFactory
    (
          ServiceEventStore events
        , IExternalAccountingGateway accounting
        , IPayrollLedgerSource payrollLedger
    )
    {
        this.events = events;
        this.accounting = accounting;
        this.payrollLedger = payrollLedger;
    }

    public async Task<CustomerAccountSnapshot> BuildAsync
    (
          int customerId
        , int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        IReadOnlyDictionary<int, MonthlyAllocationServiceEvent> allocations = await this.events.GetCurrentMonthlyAllocationsAsync
        (
              year
            , month
            , cancellationToken
        );

        if (!allocations.TryGetValue(customerId, out MonthlyAllocationServiceEvent? allocationEvent))
        {
            throw new InvalidOperationException($"Month {year:D4}-{month:D2} has not been opened for employee {customerId}.");
        }

        MonthlyCreditAllocation allocation = new
        (
              allocationEvent.CustomerId
            , allocationEvent.Year
            , allocationEvent.Month
            , allocationEvent.EligibleWorkingDays
            , allocationEvent.IsEligibleForCredit
            , allocationEvent.CreditLimitPrice
            , allocationEvent.SubsidyCost
            , allocationEvent.CalculatedAt
        );

        IReadOnlyList<ExternalAccountSummary> accountSummaries = await this.accounting.GetAccountSummariesAsync(cancellationToken);
        ExternalAccountSummary account = accountSummaries.SingleOrDefault(summary => summary.CustomerId == customerId)
            ?? throw new InvalidOperationException($"FFP account summary was not found for employee {customerId}.");

        IReadOnlyDictionary<int, PayrollLedgerTotals> ledgerTotals = await this.payrollLedger.GetMonthlyTotalsAsync
        (
              year
            , month
            , cancellationToken
        );

        PayrollLedgerTotals ledger = ledgerTotals.TryGetValue(customerId, out PayrollLedgerTotals? existingLedger)
            ? existingLedger
            : PayrollLedgerTotals.Empty;

        decimal netCafeteriaCharges = Math.Max(0m, ledger.Purchases - ledger.Refunds);
        decimal existingStipendReduction = ledger.StipendCardReduction;
        decimal baseCreditAfterStipends = Math.Max(0m, allocation.TotalCreditLimit - existingStipendReduction);
        decimal subsidyUsedByPurchases = Math.Min(netCafeteriaCharges, baseCreditAfterStipends);
        decimal companyContributionUsed = Math.Min
        (
              allocation.SubsidyAllocation
            , existingStipendReduction + subsidyUsedByPurchases
        );
        decimal remainingCompanyContribution = Math.Max(0m, allocation.SubsidyAllocation - companyContributionUsed);

        return new CustomerAccountSnapshot
        (
              BaseMonthlyCredit: allocation.TotalCreditLimit
            , PriorPeriodOutstandingSettlements: 0m
            , StipendCashPaid: 0m
            , StipendCardReduction: existingStipendReduction
            , BaseCreditAvailableForCurrentPurchases: Math.Max(0m, baseCreditAfterStipends - netCafeteriaCharges)
            , AdditionalCreditApproved: Math.Max(0m, account.CreditLimit - allocation.TotalCreditLimit)
            , TotalSpendingCapacity: account.CreditLimit
            , TotalCharges: netCafeteriaCharges
            , CompanyResponsibility: Math.Max(0m, companyContributionUsed - existingStipendReduction)
            , CompanyContributionUsed: companyContributionUsed
            , RemainingCompanyContribution: remainingCompanyContribution
            , CustomerResponsibilityForCurrentCharges: Math.Max(0m, netCafeteriaCharges - Math.Max(0m, companyContributionUsed - existingStipendReduction))
            , CustomerPayments: ledger.CustomerPayments
            , EmployeeAmountDue: 0m
            , AvailableCredit: Math.Max(0m, account.AvailableBalance)
            , AdditionalCreditUsed: 0m
            , UnusedAdditionalCredit: 0m
            , TotalOrderSales: netCafeteriaCharges
            , CafeteriaAccountTendered: ledger.Purchases
            , TotalRefunds: ledger.Refunds
            , CafeteriaAccountRefunded: ledger.Refunds
            , MonthlyEmployeeFundedAllocation: allocation.EmployeeFundedAllocation
            , MonthlySubsidyAllocation: allocation.SubsidyAllocation
        );
    }
}
