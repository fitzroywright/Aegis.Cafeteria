namespace Aegis.Cafeteria.Application.Abstractions;

public interface IHolidayProvider
{
    Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default);
}
