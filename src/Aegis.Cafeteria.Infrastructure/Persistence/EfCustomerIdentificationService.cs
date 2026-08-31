namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Abstractions;
using Aegis.Cafeteria.Application.Reporting;
using Aegis.Cafeteria.Domain.Identification;
using Microsoft.EntityFrameworkCore;

public sealed class EfCustomerIdentificationService(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    ICustomerAccountQuery accountQuery) : ICustomerIdentificationService
{
    public async Task<CustomerAccountView?> IdentifyAsync(CustomerIdentifierType type, string value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        string normalized = DomainPersistenceMapper.NormalizeIdentifier(type, value);
        if (string.IsNullOrWhiteSpace(normalized)) return null;
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        int? customerId = await db.CustomerIdentifiers.AsNoTracking()
            .Where(x => x.Type == (int)type && x.NormalizedValue == normalized)
            .Select(x => (int?)x.CustomerId)
            .SingleOrDefaultAsync(cancellationToken);
        if (!customerId.HasValue) return null;
        return await accountQuery.GetCurrentAsync(customerId.Value, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerSearchCandidate>> SearchAsync(string searchText, int maximumResults = 20, CancellationToken cancellationToken = default)
    {
        maximumResults = Math.Clamp(maximumResults, 1, 50);
        string rawSearch = searchText?.Trim() ?? string.Empty;
        string normalized = DomainPersistenceMapper.NormalizeSearchText(rawSearch);

        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);

        List<int> matchedIds;
        if (rawSearch.Length == 0)
        {
            matchedIds = await db.Customers.AsNoTracking()
                .Where(x => x.IsEligibleForCredit)
                .OrderBy(x => x.NormalizedName)
                .Select(x => x.Id)
                .Take(maximumResults)
                .ToListAsync(cancellationToken);
        }
        else
        {
            // Directory search is intentionally name-only. Secret identifiers are accepted only
            // by IdentifyAsync and are never echoed into a browse/search result.
            matchedIds = await db.Customers.AsNoTracking()
                .Where(x => x.IsEligibleForCredit && x.NormalizedName.Contains(normalized))
                .OrderBy(x => x.NormalizedName)
                .Select(x => x.Id)
                .Take(maximumResults)
                .ToListAsync(cancellationToken);
        }

        if (matchedIds.Count == 0) return [];

        List<Models.CustomerRow> customers = await db.Customers.AsNoTracking()
            .Where(x => matchedIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        // Secret identifiers are deliberately not projected into UI-facing search results.
        // They remain in the local identifier cache and are used only for matching.
        return matchedIds
            .Select(id => customers.Single(x => x.Id == id))
            .Select(x => new CustomerSearchCandidate(
                x.Id,
                x.Name,
                x.IsEligibleForCredit,
                x.Photo,
                x.Department,
                x.Manager,
                x.EmailAddress,
                x.Extension))
            .ToArray();
    }

    public Task<CustomerAccountView?> LoadAsync(int customerId, CancellationToken cancellationToken = default)
        => accountQuery.GetCurrentAsync(customerId, cancellationToken);
}
