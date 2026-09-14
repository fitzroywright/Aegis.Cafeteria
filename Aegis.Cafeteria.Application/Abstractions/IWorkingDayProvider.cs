namespace Aegis.Cafeteria.Application.Abstractions;

public interface IWorkingDayProvider
{
    Task<int> GetWorkingDaysAsync(int year, int month, CancellationToken cancellationToken = default);

    Task<int> GetWorkingDaysAsync(
        int year,
        int month,
        DateOnly? notBefore,
        CancellationToken cancellationToken = default)
        => GetWorkingDaysAsync(year, month, cancellationToken);
}
