namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Domain.Services;
using Aegis.Cafeteria.Services.Persistence;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

public sealed class MonthOpeningWorker : BackgroundService
{
    private readonly IWorkforceReferenceSource workforce;
    private readonly ICafeteriaFundingPolicySource fundingPolicy;
    private readonly MonthlyAllocationCalculator calculator;
    private readonly ServiceEventStore events;
    private readonly IExternalAccountingGateway accounting;
    private readonly IMonthRecalculationGuard recalculationGuard;
    private readonly MonthOpeningOptions options;
    private readonly ILogger<MonthOpeningWorker> logger;

    public MonthOpeningWorker
    (
          IWorkforceReferenceSource workforce
        , ICafeteriaFundingPolicySource fundingPolicy
        , MonthlyAllocationCalculator calculator
        , ServiceEventStore events
        , IExternalAccountingGateway accounting
        , IMonthRecalculationGuard recalculationGuard
        , MonthOpeningOptions options
        , ILogger<MonthOpeningWorker> logger
    )
    {
        this.workforce = workforce;
        this.fundingPolicy = fundingPolicy;
        this.calculator = calculator;
        this.events = events;
        this.accounting = accounting;
        this.recalculationGuard = recalculationGuard;
        this.options = options;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync
    (
        CancellationToken stoppingToken
    )
    {
        if (!this.options.Enabled)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTime now = DateTime.Now;

                if (now.Hour >= this.options.RunHourLocal)
                {
                    await EnsureMonthAsync
                    (
                          now.Year
                        , now.Month
                        , stoppingToken
                    );
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                this.logger.LogError
                (
                      exception
                    , "Cafeteria month-opening allocation job failed; it will retry"
                );
            }

            await Task.Delay
            (
                  TimeSpan.FromMinutes(30)
                , stoppingToken
            );
        }
    }

    internal async Task<int> EnsureMonthAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken
    )
    {
        IReadOnlyList<ExternalCustomerSnapshot> employees = await this.workforce.GetCustomersAsync(cancellationToken);
        IReadOnlyCollection<DateOnly> holidays = await this.workforce.GetHolidaysAsync(year, month, cancellationToken);
        CafeteriaFundingPolicySnapshot policy = await this.fundingPolicy.GetFundingPolicyAsync(cancellationToken);
        int workingDays = this.calculator.CountWorkingDays(year, month, holidays);
        int created = 0;

        foreach (ExternalCustomerSnapshot employee in employees.Where(employee => employee.IsEligibleForCafeteriaCredit))
        {
            Guid operationId = DeterministicOperationId(employee.CustomerId, year, month);

            if (await this.events.HasOperationAsync(operationId, cancellationToken))
            {
                continue;
            }

            MonthlyCreditAllocation allocation = this.calculator.Calculate
            (
                  employee.CustomerId
                , year
                , month
                , workingDays
                , true
                , policy.DailyCreditLimitRate
                , policy.DailyCompanySubsidyRate
                , DateTimeOffset.UtcNow
            );

            MonthlyAllocationServiceEvent payload = ToServiceEvent
            (
                  operationId
                , employee.Name
                , allocation
            );

            await this.events.AppendAsync
            (
                  operationId
                , CafeteriaServiceEventKind.MonthlyAllocation
                , employee.CustomerId
                , allocation.CalculatedAt
                , payload
                , cancellationToken
            );

            created++;
        }

        if (created > 0)
        {
            this.logger.LogInformation
            (
                  "Created {Count} Cafeteria month-opening allocation events for {Year}-{Month:D2}"
                , created
                , year
                , month
            );
        }

        return created;
    }

    internal async Task<MonthRecalculationResult> RecalculateMonthAsync
    (
          int year
        , int month
        , string recalculatedBy
        , CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(recalculatedBy))
        {
            throw new ArgumentException("A management identity is required.", nameof(recalculatedBy));
        }

        MonthRecalculationGuardResult guard = await this.recalculationGuard.CheckAsync
        (
              year
            , month
            , cancellationToken
        );

        if (!guard.IsSafe)
        {
            throw new InvalidOperationException
            (
                guard.Reason ?? $"Month {year:D4}-{month:D2} cannot be recalculated because cafeteria financial activity exists."
            );
        }

        IReadOnlyDictionary<int, MonthlyAllocationServiceEvent> existingAllocations = await this.events.GetCurrentMonthlyAllocationsAsync
        (
              year
            , month
            , cancellationToken
        );

        if (existingAllocations.Count == 0)
        {
            throw new InvalidOperationException
            (
                $"Month {year:D4}-{month:D2} has not been opened. Run Month Opening instead of Recalculate Month."
            );
        }

        IReadOnlyList<ExternalCustomerSnapshot> employees = (await this.workforce.GetCustomersAsync(cancellationToken))
            .Where(employee => employee.IsEligibleForCafeteriaCredit)
            .OrderBy(employee => employee.CustomerId)
            .ToArray();

        int[] existingEmployeeIds = existingAllocations.Keys.OrderBy(customerId => customerId).ToArray();
        int[] currentEmployeeIds = employees.Select(employee => employee.CustomerId).OrderBy(customerId => customerId).ToArray();

        if (!existingEmployeeIds.SequenceEqual(currentEmployeeIds))
        {
            throw new InvalidOperationException
            (
                "The eligible employee set has changed since Month Opening. Recalculation is blocked because it would add or remove employee entitlements."
            );
        }

        IReadOnlyDictionary<int, ExternalAccountSummary> accounts = (await this.accounting.GetAccountSummariesAsync(cancellationToken))
            .ToDictionary(account => account.CustomerId);

        foreach (KeyValuePair<int, MonthlyAllocationServiceEvent> entry in existingAllocations)
        {
            MonthlyCreditAllocation currentAllocation = ToAllocation(entry.Value);

            if (!accounts.TryGetValue(entry.Key, out ExternalAccountSummary account))
            {
                throw new InvalidOperationException
                (
                    $"FFP account summary was not found for employee {entry.Key}. Recalculation was not performed."
                );
            }

            if (account.CreditLimit != currentAllocation.TotalCreditLimit || account.AvailableBalance != currentAllocation.TotalCreditLimit)
            {
                throw new InvalidOperationException
                (
                    $"Employee {entry.Key} no longer has the untouched Month Opening balance. "
                    + $"Expected CreditLimit/AvailableBalance {currentAllocation.TotalCreditLimit:N2}/{currentAllocation.TotalCreditLimit:N2}, "
                    + $"but FFP contains {account.CreditLimit:N2}/{account.AvailableBalance:N2}. Recalculation is blocked."
                );
            }
        }

        IReadOnlyCollection<DateOnly> holidays = await this.workforce.GetHolidaysAsync(year, month, cancellationToken);
        CafeteriaFundingPolicySnapshot policy = await this.fundingPolicy.GetFundingPolicyAsync(cancellationToken);
        int workingDays = this.calculator.CountWorkingDays(year, month, holidays);
        int updated = 0;
        int unchanged = 0;

        foreach (ExternalCustomerSnapshot employee in employees)
        {
            MonthlyAllocationServiceEvent previous = existingAllocations[employee.CustomerId];
            DateTimeOffset calculatedAt = DateTimeOffset.UtcNow;
            MonthlyCreditAllocation replacementAllocation = this.calculator.Calculate
            (
                  employee.CustomerId
                , year
                , month
                , workingDays
                , true
                , policy.DailyCreditLimitRate
                , policy.DailyCompanySubsidyRate
                , calculatedAt
            );

            MonthlyAllocationServiceEvent replacement = ToServiceEvent
            (
                  DeterministicRecalculationOperationId
                  (
                        employee.CustomerId
                      , year
                      , month
                      , workingDays
                      , policy.DailyCreditLimitRate
                      , policy.DailyCompanySubsidyRate
                  )
                , employee.Name
                , replacementAllocation
            );

            if (Equivalent(previous, replacement))
            {
                unchanged++;
                continue;
            }

            if (await this.events.HasOperationAsync(replacement.OperationId, cancellationToken))
            {
                unchanged++;
                continue;
            }

            MonthlyAllocationRecalculationServiceEvent payload = new
            (
                  replacement.OperationId
                , previous
                , replacement
                , recalculatedBy.Trim()
                , calculatedAt
            );

            await this.events.AppendAsync
            (
                  replacement.OperationId
                , CafeteriaServiceEventKind.MonthlyAllocationRecalculation
                , employee.CustomerId
                , calculatedAt
                , payload
                , cancellationToken
            );

            updated++;
        }

        this.logger.LogWarning
        (
              "Recalculated Cafeteria month {Year}-{Month:D2}: {Updated} employee allocations replaced, {Unchanged} unchanged, actor {Actor}"
            , year
            , month
            , updated
            , unchanged
            , recalculatedBy
        );

        return new MonthRecalculationResult
        (
              year
            , month
            , employees.Count
            , updated
            , unchanged
            , workingDays
            , policy.DailyCreditLimitRate
            , policy.DailyCompanySubsidyRate
            , recalculatedBy.Trim()
        );
    }

    private static MonthlyAllocationServiceEvent ToServiceEvent
    (
          Guid operationId
        , string employeeName
        , MonthlyCreditAllocation allocation
    )
    {
        return new MonthlyAllocationServiceEvent
        (
              operationId
            , allocation.CustomerId
            , allocation.Year
            , allocation.Month
            , allocation.EligibleWorkingDays
            , allocation.IsEligibleForCredit
            , allocation.CreditLimitPrice
            , allocation.SubsidyCost
            , allocation.CalculatedAt
            , employeeName
        );
    }

    private static MonthlyCreditAllocation ToAllocation
    (
        MonthlyAllocationServiceEvent allocation
    )
    {
        return new MonthlyCreditAllocation
        (
              allocation.CustomerId
            , allocation.Year
            , allocation.Month
            , allocation.EligibleWorkingDays
            , allocation.IsEligibleForCredit
            , allocation.CreditLimitPrice
            , allocation.SubsidyCost
            , allocation.CalculatedAt
        );
    }

    private static bool Equivalent
    (
          MonthlyAllocationServiceEvent left
        , MonthlyAllocationServiceEvent right
    )
    {
        return left.CustomerId == right.CustomerId
            && left.Year == right.Year
            && left.Month == right.Month
            && left.EligibleWorkingDays == right.EligibleWorkingDays
            && left.IsEligibleForCredit == right.IsEligibleForCredit
            && left.CreditLimitPrice == right.CreditLimitPrice
            && left.SubsidyCost == right.SubsidyCost;
    }

    private static Guid DeterministicOperationId
    (
          int customerId
        , int year
        , int month
    )
    {
        byte[] bytes = SHA256.HashData
        (
            Encoding.UTF8.GetBytes($"{customerId}:{year:D4}-{month:D2}:monthly-allocation")
        );

        return new Guid(bytes.AsSpan(0, 16));
    }

    private static Guid DeterministicRecalculationOperationId
    (
          int customerId
        , int year
        , int month
        , int workingDays
        , decimal dailyCreditLimitRate
        , decimal dailyCompanySubsidyRate
    )
    {
        string key = string.Join
        (
              ":"
            , customerId.ToString(CultureInfo.InvariantCulture)
            , $"{year:D4}-{month:D2}"
            , workingDays.ToString(CultureInfo.InvariantCulture)
            , dailyCreditLimitRate.ToString("0.00####", CultureInfo.InvariantCulture)
            , dailyCompanySubsidyRate.ToString("0.00####", CultureInfo.InvariantCulture)
            , "monthly-allocation-recalculation"
        );

        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

public sealed record MonthRecalculationResult
(
      int Year
    , int Month
    , int Examined
    , int Updated
    , int Unchanged
    , int EligibleWorkingDays
    , decimal DailyCreditLimitRate
    , decimal DailyCompanySubsidyRate
    , string RecalculatedBy
);
