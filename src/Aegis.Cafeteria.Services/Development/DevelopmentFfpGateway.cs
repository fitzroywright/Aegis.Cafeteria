namespace Aegis.Cafeteria.Services.Development;

using Aegis.Cafeteria.Application.Integration;

using System.Collections.Concurrent;

/// <summary>
/// Development-only FFP substitute for laptop integration testing.
/// It is never registered outside the Development environment.
/// </summary>
public sealed class DevelopmentFfpGateway :
      IExternalAccountingGateway
    , IWorkforceReferenceSource
    , IFundingPolicyCompatibilityGateway
    , IMonthRecalculationGuard
    , IDevelopmentDataResetGateway
{
    private readonly ConcurrentDictionary<int, ExternalAccountSummary> accounts = new();
    private readonly ConcurrentDictionary<Guid, ExternalLedgerEntry> ledgerEntries = new();

    private static readonly ExternalCustomerSnapshot[] Customers =
    [
        new ExternalCustomerSnapshot
        {
              CustomerId = 900001
            , Name = "Test Employee One"
            , IsEligibleForCafeteriaCredit = true
            , Nfc = "TEST-NFC-001"
            , Code = "TEST001"
            , Trn = "111111111"
            , Department = "Information Systems"
        },
        new ExternalCustomerSnapshot
        {
              CustomerId = 900002
            , Name = "Test Employee Two"
            , IsEligibleForCafeteriaCredit = true
            , Nfc = "TEST-NFC-002"
            , Code = "TEST002"
            , Trn = "222222222"
            , Department = "Finance"
        }
    ];

    public DevelopmentFfpGateway()
    {
        foreach (ExternalCustomerSnapshot customer in Customers)
        {
            this.accounts[customer.CustomerId] = new ExternalAccountSummary
            (
                  customer.CustomerId
                , 0m
                , 0m
            );
        }
    }

    public Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ExternalCustomerSnapshot> customers = Customers;
        return Task.FromResult(customers);
    }

    public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        IReadOnlyCollection<DateOnly> holidays = [];
        return Task.FromResult(holidays);
    }

    public Task PublishLedgerEntryAsync
    (
          ExternalLedgerEntry entry
        , CancellationToken cancellationToken = default
    )
    {
        this.ledgerEntries[entry.OperationId] = entry;

        this.accounts[entry.CustomerId] = new ExternalAccountSummary
        (
              entry.CustomerId
            , entry.CreditLimit
            , entry.AvailableBalance
        );

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ExternalAccountSummary>> GetAccountSummariesAsync
    (
        CancellationToken cancellationToken = default
    )
    {
        IReadOnlyList<ExternalAccountSummary> summaries = this.accounts.Values
            .OrderBy(account => account.CustomerId)
            .ToArray();

        return Task.FromResult(summaries);
    }

    public IReadOnlyList<ExternalLedgerEntry> GetPublishedLedgerEntries()
    {
        return this.ledgerEntries.Values
            .OrderBy(entry => entry.OccurredAt)
            .ThenBy(entry => entry.OperationId)
            .ToArray();
    }

    public Task<MonthRecalculationGuardResult> CheckAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        int activityCount = this.ledgerEntries.Values.Count
        (
            entry =>
                   entry.OccurredAt.Year == year
                && entry.OccurredAt.Month == month
                && entry.Kind is not ExternalLedgerEntryKind.MonthlyAllocation
                && !string.Equals(entry.IdName, "Monthly Allocation Recalculation", StringComparison.Ordinal)
        );

        MonthRecalculationGuardResult result = activityCount == 0
            ? new MonthRecalculationGuardResult(true, 0, null)
            : new MonthRecalculationGuardResult
            (
                  false
                , activityCount
                , $"Month {year:D4}-{month:D2} contains {activityCount} cafeteria financial transaction(s)."
            );

        return Task.FromResult(result);
    }

    public Task<DevelopmentDataResetResult> ResetAsync
    (
          string resetBy
        , DateTimeOffset resetAt
        , CancellationToken cancellationToken = default
    )
    {
        int deletedLedgerEntries = this.ledgerEntries.Count;
        this.ledgerEntries.Clear();

        foreach (ExternalCustomerSnapshot customer in Customers)
        {
            this.accounts[customer.CustomerId] = new ExternalAccountSummary
            (
                  customer.CustomerId
                , 0m
                , 0m
            );
        }

        DevelopmentDataResetResult result = new
        (
              deletedLedgerEntries
            , Customers.Length
        );

        return Task.FromResult(result);
    }

    public Task MirrorFundingPolicyAsync
    (
          decimal dailyCreditLimitRate
        , decimal dailyCompanySubsidyRate
        , CancellationToken cancellationToken = default
    )
    {
        return Task.CompletedTask;
    }
}
