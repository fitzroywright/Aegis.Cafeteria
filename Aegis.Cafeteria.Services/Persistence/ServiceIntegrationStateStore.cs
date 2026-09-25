namespace Aegis.Cafeteria.Services.Persistence;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

public sealed class ServiceIntegrationStateStore(
    IDbContextFactory<ServiceDbContext> factory) :
    IEmployeeFactCache,
    ICafeteriaPolicyCache
{
    private const string EmployeeVersionKey = "employee-feed-version";

    public async Task<EmployeeFactCacheSnapshot> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext db =
            await factory.CreateDbContextAsync(cancellationToken);

        string? version = await db.IntegrationState
            .AsNoTracking()
            .Where(x => x.Key == EmployeeVersionKey)
            .Select(x => x.Value)
            .SingleOrDefaultAsync(cancellationToken);

        IntegrationEmployeeCacheRow[] rows = await db.IntegrationEmployees
            .AsNoTracking()
            .OrderBy(x => x.EmployeeId)
            .ToArrayAsync(cancellationToken);

        return new EmployeeFactCacheSnapshot(
            version,
            rows.Select(x => new EmployeeFact(
                x.EmployeeId,
                x.Name,
                x.CardIdentifier,
                x.IsActive,
                x.IsEligible,
                x.EligibleFrom,
                x.Version)).ToArray());
    }

    public async Task SaveAsync(
        EmployeeFactCacheSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext db =
            await factory.CreateDbContextAsync(cancellationToken);

        await using var transaction =
            await db.Database.BeginTransactionAsync(cancellationToken);

        Dictionary<int, IntegrationEmployeeCacheRow> existing =
            await db.IntegrationEmployees.ToDictionaryAsync(
                x => x.EmployeeId,
                cancellationToken);

        foreach (EmployeeFact employee in snapshot.Employees)
        {
            if (!existing.TryGetValue(employee.EmployeeId, out IntegrationEmployeeCacheRow? row))
            {
                row = new IntegrationEmployeeCacheRow
                {
                    EmployeeId = employee.EmployeeId
                };
                db.IntegrationEmployees.Add(row);
            }

            row.Name = employee.Name;
            row.CardIdentifier = employee.CardIdentifier;
            row.IsActive = employee.IsActive;
            row.IsEligible = employee.IsEligible;
            row.EligibleFrom = employee.EligibleFrom;
            row.Version = employee.Version;
        }

        IntegrationStateRow? state = await db.IntegrationState
            .SingleOrDefaultAsync(
                x => x.Key == EmployeeVersionKey,
                cancellationToken);

        if (state is null)
        {
            state = new IntegrationStateRow { Key = EmployeeVersionKey };
            db.IntegrationState.Add(state);
        }

        state.Value = snapshot.Version;
        state.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<CafeteriaPolicySnapshot?> GetAsync(
        int year,
        int month,
        CancellationToken cancellationToken = default)
    {
        await using ServiceDbContext db =
            await factory.CreateDbContextAsync(cancellationToken);

        string? json = await db.ValidatedPolicies
            .AsNoTracking()
            .Where(x => x.Year == year && x.Month == month)
            .Select(x => x.PayloadJson)
            .SingleOrDefaultAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(json))
            return null;

        CafeteriaPolicySnapshot? policy =
            JsonSerializer.Deserialize<CafeteriaPolicySnapshot>(json);

        policy?.Validate();
        return policy;
    }

    public async Task SaveAsync(
        CafeteriaPolicySnapshot policy,
        CancellationToken cancellationToken = default)
    {
        policy.Validate();

        await using ServiceDbContext db =
            await factory.CreateDbContextAsync(cancellationToken);

        ValidatedPolicyCacheRow? row = await db.ValidatedPolicies
            .SingleOrDefaultAsync(
                x => x.Year == policy.Year && x.Month == policy.Month,
                cancellationToken);

        if (row is null)
        {
            row = new ValidatedPolicyCacheRow
            {
                Year = policy.Year,
                Month = policy.Month
            };
            db.ValidatedPolicies.Add(row);
        }

        row.PayloadJson = JsonSerializer.Serialize(policy);
        row.ValidatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }
}
