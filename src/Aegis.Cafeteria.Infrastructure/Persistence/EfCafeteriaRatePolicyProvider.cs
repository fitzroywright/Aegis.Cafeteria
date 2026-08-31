namespace Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Domain.Accounts;
using Microsoft.EntityFrameworkCore;
public sealed class EfCafeteriaRatePolicyProvider(IDbContextFactory<LocalCafeteriaDbContext> factory) : ICafeteriaRatePolicyProvider
{
    public async Task<CafeteriaRatePolicy?> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.CafeteriaRatePolicies.AsNoTracking().SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        return row is null ? null : new CafeteriaRatePolicy(row.CreditLimitLunchItemCode, row.CreditLimitPrice, row.SubsidyLunchItemCode, row.SubsidyCost, row.RefreshedAt);
    }
}
