namespace Aegis.Cafeteria.Plugin.Integration;

using Aegis.Cafeteria.Application.Integration;

public sealed class IntegrationWorkforceReferenceSource : IWorkforceReferenceSource
{
    private readonly IEmployeeFactSource employeeFacts;
    private readonly ICafeteriaPolicyProvider policyProvider;
    private readonly IEmployeeFactCache employeeCache;
    private readonly SemaphoreSlim gate = new(1, 1);

    public IntegrationWorkforceReferenceSource(
        IEmployeeFactSource employeeFacts,
        ICafeteriaPolicyProvider policyProvider,
        IEmployeeFactCache employeeCache)
    {
        this.employeeFacts = employeeFacts;
        this.policyProvider = policyProvider;
        this.employeeCache = employeeCache;
    }

    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        EmployeeFactCacheSnapshot cache = await RefreshAsync(cancellationToken);

        return cache.Employees
            .OrderBy(employee => employee.EmployeeId)
            .Select(employee => new ExternalCustomerSnapshot
            {
                  CustomerId = employee.EmployeeId
                , Name = employee.Name
                , Nfc = employee.CardIdentifier
                , IsActive = employee.IsActive
                , IsEligibleForCafeteriaCredit = employee.IsEligible
                , EmploymentStartDate = employee.EligibleFrom
            })
            .ToArray();
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        CafeteriaPolicySnapshot policy = await this.policyProvider.GetPolicyAsync(
            year,
            month,
            cancellationToken: cancellationToken);

        HashSet<DateOnly> workingDays = policy.WorkingDays.ToHashSet();
        DateOnly start = new(year, month, 1);
        DateOnly end = new(year, month, DateTime.DaysInMonth(year, month));
        List<DateOnly> excludedWeekdays = [];

        for (DateOnly date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            if (!workingDays.Contains(date))
                excludedWeekdays.Add(date);
        }

        return excludedWeekdays;
    }

    private async Task<EmployeeFactCacheSnapshot> RefreshAsync(
        CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            EmployeeFactCacheSnapshot cache =
                await this.employeeCache.LoadAsync(cancellationToken);

            EmployeeFactChangeSet changes =
                await this.employeeFacts.GetChangesAsync(
                    cache.Version,
                    cancellationToken);

            Dictionary<int, EmployeeFact> employees =
                cache.Employees.ToDictionary(employee => employee.EmployeeId);

            foreach (EmployeeFact employee in changes.Employees)
                employees[employee.EmployeeId] = employee;

            EmployeeFactCacheSnapshot updated = new(
                changes.Version,
                employees.Values
                    .OrderBy(employee => employee.EmployeeId)
                    .ToArray());

            await this.employeeCache.SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            this.gate.Release();
        }
    }
}
