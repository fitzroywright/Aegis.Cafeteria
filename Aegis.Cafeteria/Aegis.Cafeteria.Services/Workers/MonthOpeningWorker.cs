namespace Aegis.Cafeteria.Services.Workers;

using Aegis.Cafeteria.Application.Accounts;
using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Accounts;
using Aegis.Cafeteria.Services.Management;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public sealed class MonthOpeningWorker : BackgroundService
{
    private readonly IWorkforceReferenceSource workforce;
    private readonly IExternalAccountingGateway accounting;
    private readonly IMonthRecalculationGuard recalculationGuard;
    private readonly ICafeteriaRatePolicyProvider ratePolicy;
    private readonly MonthlyAllocationCalculator calculator;
    private readonly MonthlyAllocationServiceLedger ledger;
    private readonly ILogger<MonthOpeningWorker> logger;
    private readonly TimeProvider timeProvider;

    public MonthOpeningWorker(
        IWorkforceReferenceSource workforce,
        IExternalAccountingGateway accounting,
        IMonthRecalculationGuard recalculationGuard,
        ICafeteriaRatePolicyProvider ratePolicy,
        MonthlyAllocationCalculator calculator,
        MonthlyAllocationServiceLedger ledger,
        ILogger<MonthOpeningWorker> logger,
        TimeProvider? timeProvider = null)
    {
        this.workforce = workforce;
        this.accounting = accounting;
        this.recalculationGuard = recalculationGuard;
        this.ratePolicy = ratePolicy;
        this.calculator = calculator;
        this.ledger = ledger;
        this.logger = logger;
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                DateTimeOffset now = this.timeProvider.GetUtcNow();
                await this.EnsureMonthOpenedAsync(now.Year, now.Month, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "Month Opening worker failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(15), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    public async Task<IReadOnlyList<MonthlyAllocationServiceEvent>> EnsureMonthOpenedAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(year, month);

        IReadOnlyList<MonthlyAllocationServiceEvent> existing = await this.ledger.GetMonthOpeningEventsAsync(year, month, cancellationToken);
        if (existing.Count > 0)
        {
            return existing;
        }

        CafeteriaRatePolicy? policy = await this.ratePolicy.GetAsync(cancellationToken);
        if (policy is null)
        {
            this.logger.LogWarning("Month Opening {Year}-{Month:00} was skipped because no cafeteria rate policy is available.", year, month);
            return [];
        }

        IReadOnlyList<ExternalCustomerSnapshot> employees = (await this.workforce.GetCustomersAsync(cancellationToken))
            .Where(employee => employee.IsActive && employee.IsEligibleForCafeteriaCredit)
            .OrderBy(employee => employee.CustomerId)
            .ToArray();
        IReadOnlyCollection<DateOnly> holidays = await this.workforce.GetHolidaysAsync(year, month, cancellationToken);

        List<MonthlyAllocationServiceEvent> opened = [];
        foreach (ExternalCustomerSnapshot employee in employees)
        {
            int employeeWorkingDays = this.calculator.CountEligibleWorkingDays(year, month, holidays, employee.EmploymentStartDate);
            MonthlyCreditAllocation allocation = this.calculator.Calculate(
                employee.CustomerId,
                year,
                month,
                employeeWorkingDays,
                true,
                policy.CreditLimitPrice,
                policy.SubsidyCost,
                this.timeProvider.GetUtcNow());

            MonthlyAllocationServiceEvent serviceEvent = MonthlyAllocationServiceEvent.FromAllocation(allocation);
            await this.accounting.PublishMonthlyAllocationAsync(employee, allocation, cancellationToken);
            await this.ledger.AppendAsync(serviceEvent, cancellationToken);
            opened.Add(serviceEvent);
        }

        return opened;
    }

    public async Task<IReadOnlyList<MonthlyAllocationServiceEvent>> RecalculateMonthOpeningAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        ValidatePeriod(year, month);

        MonthRecalculationGuardResult guard = await this.recalculationGuard.CheckAsync(year, month, cancellationToken);
        if (!guard.Allowed)
        {
            throw new InvalidOperationException(guard.Reason);
        }

        IReadOnlyList<MonthlyAllocationServiceEvent> existingEvents = await this.ledger.GetMonthOpeningEventsAsync(year, month, cancellationToken);
        if (existingEvents.Count == 0)
        {
            throw new InvalidOperationException("Month Opening has not yet been created for the requested month.");
        }

        Dictionary<int, MonthlyAllocationServiceEvent> existingAllocations = existingEvents
            .GroupBy(x => x.CustomerId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(x => x.OccurredAt).First());

        CafeteriaRatePolicy? policy = await this.ratePolicy.GetAsync(cancellationToken);
        if (policy is null)
        {
            throw new InvalidOperationException("Cafeteria rate policy is not available. Recalculation was not performed.");
        }

        ExternalCustomerSnapshot[] employees = (await this.workforce.GetCustomersAsync(cancellationToken))
            .Where(employee => employee.IsActive && employee.IsEligibleForCafeteriaCredit)
            .OrderBy(employee => employee.CustomerId)
            .ToArray();

        int[] existingEmployeeIds = existingAllocations.Keys.OrderBy(customerId => customerId).ToArray();
        int[] currentEmployeeIds = employees.Select(employee => employee.CustomerId).OrderBy(customerId => customerId).ToArray();

        if (!existingEmployeeIds.SequenceEqual(currentEmployeeIds))
        {
            throw new InvalidOperationException(
                "The eligible employee set has changed since Month Opening. Recalculation is blocked because it would add or remove employee entitlements.");
        }

        IReadOnlyDictionary<int, ExternalAccountSummary> accounts = (await this.accounting.GetAccountSummariesAsync(cancellationToken))
            .ToDictionary(account => account.CustomerId);

        foreach (KeyValuePair<int, MonthlyAllocationServiceEvent> entry in existingAllocations)
        {
            MonthlyCreditAllocation currentAllocation = ToAllocation(entry.Value);
            if (!accounts.TryGetValue(entry.Key, out ExternalAccountSummary? account) || account is null)
            {
                throw new InvalidOperationException($"FFP account summary was not found for employee {entry.Key}. Recalculation was not performed.");
            }

            if (account.CreditLimit != currentAllocation.TotalCreditLimit || account.AvailableBalance != currentAllocation.TotalCreditLimit)
            {
                throw new InvalidOperationException(
                    $"Employee {entry.Key} no longer has the untouched Month Opening balance. " +
                    $"Expected CreditLimit/AvailableBalance {currentAllocation.TotalCreditLimit:N2}/{currentAllocation.TotalCreditLimit:N2}, " +
                    $"but FFP contains {account.CreditLimit:N2}/{account.AvailableBalance:N2}. Recalculation is blocked.");
            }
        }

        IReadOnlyCollection<DateOnly> holidays = await this.workforce.GetHolidaysAsync(year, month, cancellationToken);
        List<MonthlyAllocationServiceEvent> recalculated = [];

        foreach (ExternalCustomerSnapshot employee in employees)
        {
            int employeeWorkingDays = this.calculator.CountEligibleWorkingDays(year, month, holidays, employee.EmploymentStartDate);
            MonthlyCreditAllocation allocation = this.calculator.Calculate(
                employee.CustomerId,
                year,
                month,
                employeeWorkingDays,
                true,
                policy.CreditLimitPrice,
                policy.SubsidyCost,
                this.timeProvider.GetUtcNow());

            MonthlyAllocationServiceEvent serviceEvent = MonthlyAllocationServiceEvent.FromAllocation(allocation);
            await this.accounting.PublishMonthlyAllocationAsync(employee, allocation, cancellationToken);
            await this.ledger.AppendAsync(serviceEvent, cancellationToken);
            recalculated.Add(serviceEvent);
        }

        return recalculated;
    }

    private static MonthlyCreditAllocation ToAllocation(MonthlyAllocationServiceEvent serviceEvent) =>
        new(
            serviceEvent.CustomerId,
            serviceEvent.Year,
            serviceEvent.Month,
            serviceEvent.WorkingDays,
            true,
            serviceEvent.TotalCreditLimit,
            serviceEvent.SubsidyAllocation,
            serviceEvent.OccurredAt);

    private static void ValidatePeriod(int year, int month)
    {
        if (year < 2000 || year > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));
    }
}
