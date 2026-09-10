namespace Aegis.Cafeteria.Application.Abstractions;

public interface IWorkingDayProvider
{
    Task<int> GetWorkingDaysAsync(int year, int month, CancellationToken cancellationToken = default);
}
