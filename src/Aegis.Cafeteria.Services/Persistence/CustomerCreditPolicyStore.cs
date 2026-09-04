namespace Aegis.Cafeteria.Services.Persistence;

using Microsoft.EntityFrameworkCore;

public sealed class CustomerCreditPolicyStore
{
    private readonly IDbContextFactory<ServiceDbContext> factory;
    private readonly AuditTrailService auditTrail;

    public CustomerCreditPolicyStore(IDbContextFactory<ServiceDbContext> factory)
    {
        this.factory = factory;
        this.auditTrail = new AuditTrailService(factory);
    }

    public async Task<decimal?> GetAdditionalCreditLimitAsync(int customerId, CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext db = await this.factory.CreateDbContextAsync(cancellationToken);
        return await db.CustomerCreditPolicies.AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .Select(x => (decimal?)x.AdditionalCreditLimit)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> GetAllAdditionalCreditLimitsAsync(CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext db = await this.factory.CreateDbContextAsync(cancellationToken);
        return await db.CustomerCreditPolicies.AsNoTracking().ToDictionaryAsync(x => x.CustomerId, x => x.AdditionalCreditLimit, cancellationToken);
    }

    public async Task<CustomerCreditPolicyRow> SetAdditionalCreditLimitAsync(int customerId, decimal limit, string updatedBy, CancellationToken cancellationToken = default)
    {
        if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId));
        if (limit < 0m) throw new ArgumentOutOfRangeException(nameof(limit));
        if (string.IsNullOrWhiteSpace(updatedBy)) throw new ArgumentException("Management identity is required.", nameof(updatedBy));

        await using ServiceDbContext db = await this.factory.CreateDbContextAsync(cancellationToken);
        CustomerCreditPolicyRow? row = await db.CustomerCreditPolicies.SingleOrDefaultAsync(x => x.CustomerId == customerId, cancellationToken);
        object? before = row is null
            ? null
            : new { row.CustomerId, row.AdditionalCreditLimit, row.UpdatedBy, row.UpdatedAt };

        if (row is null)
        {
            row = new CustomerCreditPolicyRow { CustomerId = customerId };
            db.CustomerCreditPolicies.Add(row);
        }

        row.AdditionalCreditLimit = limit;
        row.UpdatedBy = updatedBy.Trim();
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await this.auditTrail.RecordAsync
            (
                  row.UpdatedBy
                , "SetAdditionalCreditLimit"
                , "CustomerCreditPolicy"
                , customerId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                , customerId
                , before: before
                , after: new { row.CustomerId, row.AdditionalCreditLimit, row.UpdatedBy, row.UpdatedAt }
                , cancellationToken: cancellationToken
            );
        }
        catch
        {
        }

        return row;
    }
}
