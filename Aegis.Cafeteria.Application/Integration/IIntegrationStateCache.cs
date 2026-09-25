namespace Aegis.Cafeteria.Application.Integration;

public interface IEmployeeFactCache
{
    Task<EmployeeFactCacheSnapshot> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        EmployeeFactCacheSnapshot snapshot,
        CancellationToken cancellationToken = default);
}

public sealed record EmployeeFactCacheSnapshot(
    string? Version,
    IReadOnlyList<EmployeeFact> Employees);

public interface ICafeteriaPolicyCache
{
    Task<CafeteriaPolicySnapshot?> GetAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        CafeteriaPolicySnapshot policy,
        CancellationToken cancellationToken = default);
}
