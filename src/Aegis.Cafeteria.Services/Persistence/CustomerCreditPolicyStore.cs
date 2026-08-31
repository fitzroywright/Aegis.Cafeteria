namespace Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

public sealed class CustomerCreditPolicyStore(IDbContextFactory<ServiceDbContext> factory)
{
    public async Task<decimal?> GetAdditionalCreditLimitAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.CustomerCreditPolicies.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .Select(x => (decimal?)x.AdditionalCreditLimit)
            .SingleOrDefaultAsync(cancellationToken);
    }


    public async Task<IReadOnlyDictionary<int, decimal>> GetAllAdditionalCreditLimitsAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.CustomerCreditPolicies.AsNoTracking().ToDictionaryAsync(x => x.CustomerId, x => x.AdditionalCreditLimit, cancellationToken);
    }

    public async Task<CustomerCreditPolicyRow> SetAdditionalCreditLimitAsync(int customerId, decimal limit, string updatedBy, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (limit < 0m) throw new ArgumentOutOfRangeException(nameof(limit));
        if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("Management identity is required.", nameof(updatedBy));

        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var row = await db.CustomerCreditPolicies.SingleOrDefaultAsync(x => x.CustomerId == customerId, cancellationToken);
        if (row is null)
        {
            row = new CustomerCreditPolicyRow { CustomerId = customerId };
            db.CustomerCreditPolicies.Add(row);
        }
        row.AdditionalCreditLimit = limit;
        row.UpdatedBy = updatedBy.Trim();
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return row;
    }
}
