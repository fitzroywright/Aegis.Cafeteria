namespace Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
public interface ICafeteriaRatePolicyProvider
{
    Task<CafeteriaRatePolicy?> GetAsync(CancellationToken cancellationToken = default);
}
