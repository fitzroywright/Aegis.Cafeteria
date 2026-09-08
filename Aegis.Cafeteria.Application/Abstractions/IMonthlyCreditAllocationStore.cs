namespace Aegis.Cafeteria.Application.Abstractions;

using Aegis.Cafeteria.Domain.Accounts;

public interface IMonthlyCreditAllocationStore
{
    Task<MonthlyCreditAllocation?> GetAsync(int customerId, int year, int month, CancellationToken cancellationToken = default);
    Task SaveAsync(MonthlyCreditAllocation allocation, CancellationToken cancellationToken = default);
}
