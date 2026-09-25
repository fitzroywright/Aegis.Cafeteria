namespace Aegis.Cafeteria.Plugin.Integration;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

public sealed class IntegrationWorkforceReferenceSource : IWorkforceReferenceSource
{
    private readonly IEmployeeFactSource employeeFacts;
    private readonly ICafeteriaPolicyProvider policyProvider;
    private readonly string employeeCachePath;
    private readonly SemaphoreSlim gate = new(1, 1);

    public IntegrationWorkforceReferenceSource(
        IEmployeeFactSource employeeFacts,
        ICafeteriaPolicyProvider policyProvider,
        IConfiguration configuration)
    {
        this.employeeFacts = employeeFacts;
        this.policyProvider = policyProvider;

        IConfigurationSection section = configuration.GetSection("IntegrationPlugin");
        this.employeeCachePath = section["EmployeeCachePath"]?.Trim()
            ?? "data/integration-employees.json";
    }

    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        EmployeeCacheDocument cache = await RefreshAsync(cancellationToken);

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

    private async Task<EmployeeCacheDocument> RefreshAsync(
        CancellationToken cancellationToken)
    {
        await this.gate.WaitAsync(cancellationToken);
        try
        {
            EmployeeCacheDocument cache = await LoadAsync(cancellationToken);
            EmployeeFactChangeSet changes = await this.employeeFacts.GetChangesAsync(
                cache.Version,
                cancellationToken);

            Dictionary<int, EmployeeFact> employees = cache.Employees.ToDictionary(
                employee => employee.EmployeeId);

            foreach (EmployeeFact employee in changes.Employees)
                employees[employee.EmployeeId] = employee;

            EmployeeCacheDocument updated = new(
                changes.Version,
                employees.Values.OrderBy(employee => employee.EmployeeId).ToArray());

            await SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            this.gate.Release();
        }
    }

    private async Task<EmployeeCacheDocument> LoadAsync(
        CancellationToken cancellationToken)
    {
        string path = ResolveCachePath();
        if (!File.Exists(path))
            return new EmployeeCacheDocument(null, []);

        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<EmployeeCacheDocument>(
            stream,
            cancellationToken: cancellationToken)
            ?? new EmployeeCacheDocument(null, []);
    }

    private async Task SaveAsync(
        EmployeeCacheDocument cache,
        CancellationToken cancellationToken)
    {
        string path = ResolveCachePath();
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = path + ".tmp";
        await using (FileStream stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                cache,
                cancellationToken: cancellationToken);
        }

        File.Move(temporaryPath, path, true);
    }

    private string ResolveCachePath()
        => Path.IsPathRooted(this.employeeCachePath)
            ? this.employeeCachePath
            : Path.Combine(AppContext.BaseDirectory, this.employeeCachePath);

    private sealed record EmployeeCacheDocument(
        string? Version,
        IReadOnlyList<EmployeeFact> Employees);
}
