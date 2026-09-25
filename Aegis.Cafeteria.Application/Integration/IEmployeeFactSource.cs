namespace Aegis.Cafeteria.Application.Integration;

public interface IEmployeeFactSource
{
    Task<EmployeeFactChangeSet> GetChangesAsync(
        string? afterVersion,
        CancellationToken cancellationToken = default);
}

public sealed record EmployeeFact(
    int EmployeeId,
    string Name,
    string? CardIdentifier,
    bool IsActive,
    bool IsEligible,
    DateOnly? EligibleFrom,
    string Version);

public sealed record EmployeeFactChangeSet(
    IReadOnlyList<EmployeeFact> Employees,
    string Version);
