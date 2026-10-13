using Aegis.Cafeteria.Application.Integration;

namespace Aegis.Cafeteria.Services.Acceptance;

// Filters real SQL-backed provider results. It does not synthesize or mock reference data.
internal sealed class AcceptanceFixtureWorkforce(IWorkforceReferenceSource source, int[] employeeIds)
    : IWorkforceReferenceSource
{
    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default) =>
        (await source.GetCustomersAsync(cancellationToken)).Where(x => employeeIds.Contains(x.CustomerId)).ToArray();
    public Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default) =>
        source.GetHolidaysAsync(year, month, cancellationToken);
}
