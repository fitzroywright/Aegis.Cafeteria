namespace Aegis.Cafeteria.Infrastructure.Persistence;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfWorkforceReferenceCache(IDbContextFactory<LocalCafeteriaDbContext> factory) : IWorkforceReferenceCache, IBatchedWorkforceReferenceCache
{
    public async Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> employees, CancellationToken cancellationToken = default)
    {
        await SaveCustomersAsync(employees, null, cancellationToken);
    }

    public Task UpsertCustomerBatchAsync(IReadOnlyCollection<ExternalCustomerSnapshot> customers, IReadOnlySet<int> previouslyRefreshedIds, CancellationToken cancellationToken = default)
        => SaveCustomersAsync(customers, previouslyRefreshedIds, cancellationToken);

    private async Task SaveCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> employees, IReadOnlySet<int>? previouslyRefreshedIds, CancellationToken cancellationToken)
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int[] batchIds = employees.Select(employee => employee.CustomerId).ToArray();
        List<CustomerRow> allCustomers = previouslyRefreshedIds is null
            ? await db.Customers.ToListAsync(cancellationToken)
            : await db.Customers.Where(customer => batchIds.Contains(customer.Id)).ToListAsync(cancellationToken);

        HashSet<int> sourceIds = employees.Select(employee => employee.CustomerId).ToHashSet();
        foreach (CustomerRow stale in allCustomers.Where(customer => previouslyRefreshedIds is null && !sourceIds.Contains(customer.Id)))
        {
            stale.IsActive = false;
            stale.IsEligibleForCredit = false;
        }

        Dictionary<string, DesiredIdentifier> desiredIdentifiers = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExternalCustomerSnapshot employee in employees)
        {
            CustomerRow? row = allCustomers.SingleOrDefault(customer => customer.Id == employee.CustomerId);
            if (row is null)
            {
                row = new CustomerRow
                {
                    Id = employee.CustomerId,
                    Revision = now.UtcDateTime.Ticks,
                    UpdatedAt = now
                };
                db.Customers.Add(row);
                allCustomers.Add(row);
            }

            row.Name = employee.Name;
            row.NormalizedName = DomainPersistenceMapper.NormalizeSearchText(employee.Name);
            row.LoginName = employee.LoginName?.Trim() ?? string.Empty;
            row.Department = employee.Department?.Trim() ?? string.Empty;
            row.Manager = employee.Manager?.Trim() ?? string.Empty;
            row.EmailAddress = employee.EmailAddress?.Trim() ?? string.Empty;
            row.Extension = employee.Extension?.Trim() ?? string.Empty;
            if (employee.Photo is not null
                && !PhotoEquals(row.Photo, employee.Photo))
            {
                row.Photo = employee.Photo;
            }
            row.IsActive = employee.IsActive;
            row.EmploymentStartDate = employee.EmploymentStartDate;
            row.IsEligibleForCredit = employee.IsActive && employee.IsEligibleForCafeteriaCredit;
            row.AdditionalCreditLimit = employee.AdditionalCreditLimit;

            AddDesiredIdentifier(desiredIdentifiers, employee.CustomerId, CustomerIdentifierType.NfcTag, employee.Nfc);
            AddDesiredIdentifier(desiredIdentifiers, employee.CustomerId, CustomerIdentifierType.Code, employee.Code);
            AddDesiredIdentifier(desiredIdentifiers, employee.CustomerId, CustomerIdentifierType.Trn, employee.Trn);
        }

        string[] desiredValues = desiredIdentifiers.Values.Select(identifier => identifier.NormalizedValue).Distinct().ToArray();
        List<CustomerIdentifierRow> existingIdentifiers = previouslyRefreshedIds is null
            ? await db.CustomerIdentifiers.ToListAsync(cancellationToken)
            : await db.CustomerIdentifiers.Where(identifier => batchIds.Contains(identifier.CustomerId)
                || desiredValues.Contains(identifier.NormalizedValue)).ToListAsync(cancellationToken);

        // Earlier batches win duplicate identifier claims, matching the ordered full refresh.
        if (previouslyRefreshedIds is not null)
        {
            foreach (CustomerIdentifierRow existing in existingIdentifiers.Where(identifier => previouslyRefreshedIds.Contains(identifier.CustomerId)))
            {
                desiredIdentifiers.Remove(BuildIdentifierClaim((CustomerIdentifierType)existing.Type, existing.NormalizedValue));
            }
        }

        db.ChangeTracker.DetectChanges();
        foreach (Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<CustomerRow> entry in
                 db.ChangeTracker.Entries<CustomerRow>().Where(entry => entry.State == EntityState.Modified))
        {
            entry.Entity.Revision = now.UtcDateTime.Ticks;
            entry.Entity.UpdatedAt = now;
        }

        Dictionary<string, CustomerIdentifierRow> existingByClaim = existingIdentifiers
            .GroupBy(identifier => BuildIdentifierClaim((CustomerIdentifierType)identifier.Type, identifier.NormalizedValue), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (CustomerIdentifierRow existing in existingIdentifiers)
        {
            string claim = BuildIdentifierClaim((CustomerIdentifierType)existing.Type, existing.NormalizedValue);
            if (previouslyRefreshedIds is not null
                && !sourceIds.Contains(existing.CustomerId)
                && !desiredIdentifiers.ContainsKey(claim))
            {
                continue;
            }
            if (!desiredIdentifiers.TryGetValue(claim, out DesiredIdentifier? desired)
                || desired.CustomerId != existing.CustomerId)
            {
                db.CustomerIdentifiers.Remove(existing);
                existingByClaim.Remove(claim);
                continue;
            }

            if (!string.Equals(existing.Value, desired.Value, StringComparison.Ordinal))
            {
                existing.Value = desired.Value;
                existing.Revision = now.UtcDateTime.Ticks;
                existing.UpdatedAt = now;
            }
        }

        foreach ((string claim, DesiredIdentifier desired) in desiredIdentifiers)
        {
            if (existingByClaim.TryGetValue(claim, out CustomerIdentifierRow? existing)
                && existing.CustomerId == desired.CustomerId)
            {
                continue;
            }

            db.CustomerIdentifiers.Add(CreateIdentifierRow(desired, now));
        }

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task CompleteCustomerRefreshAsync(IReadOnlySet<int> customerIds, CancellationToken cancellationToken = default)
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        int[] cachedIds = await db.Customers.Select(customer => customer.Id).ToArrayAsync(cancellationToken);
        int[] staleIds = cachedIds.Where(id => !customerIds.Contains(id)).ToArray();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (int[] chunk in staleIds.Chunk(100))
        {
            await db.Customers.Where(customer => chunk.Contains(customer.Id)).ExecuteUpdateAsync(setters => setters
                .SetProperty(customer => customer.IsActive, false)
                .SetProperty(customer => customer.IsEligibleForCredit, false)
                .SetProperty(customer => customer.Revision, now.UtcDateTime.Ticks)
                .SetProperty(customer => customer.UpdatedAt, now), cancellationToken);
            await db.CustomerIdentifiers.Where(identifier => chunk.Contains(identifier.CustomerId)).ExecuteDeleteAsync(cancellationToken);
        }
        await tx.CommitAsync(cancellationToken);
    }

    public async Task ReplaceHolidaysAsync(int year, int month, IReadOnlyCollection<DateOnly> holidays, CancellationToken cancellationToken = default)
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        List<HolidayRow> allHolidays = await db.Holidays.ToListAsync(cancellationToken);
        List<HolidayRow> existing = allHolidays.Where(holiday => holiday.Date.Year == year && holiday.Date.Month == month).ToList();
        db.Holidays.RemoveRange(existing);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (DateOnly holiday in holidays.Distinct())
            db.Holidays.Add(new HolidayRow { Date = holiday, Name = "Holiday", Revision = now.UtcDateTime.Ticks, UpdatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddDesiredIdentifier(
        IDictionary<string, DesiredIdentifier> desired,
        int customerId,
        CustomerIdentifierType type,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        string normalized = DomainPersistenceMapper.NormalizeIdentifier(type, value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        string claim = BuildIdentifierClaim(type, normalized);
        if (!desired.ContainsKey(claim))
        {
            desired[claim] = new DesiredIdentifier(customerId, type, value.Trim(), normalized);
        }
    }

    private static bool PhotoEquals(byte[]? existing, byte[] incoming)
    {
        if (existing is null || existing.Length != incoming.Length)
            return false;

        return existing.AsSpan().SequenceEqual(incoming);
    }

    private static string BuildIdentifierClaim(CustomerIdentifierType type, string normalized)
        => $"{(int)type}:{normalized}";

    private static CustomerIdentifierRow CreateIdentifierRow(DesiredIdentifier desired, DateTimeOffset now)
    {
        string legacyStablePrefix = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String("RkZQ"));
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"{legacyStablePrefix}:{desired.CustomerId}:{desired.Type}:{desired.NormalizedValue}"));

        return new CustomerIdentifierRow
        {
            Id = new Guid(bytes.AsSpan(0, 16)),
            CustomerId = desired.CustomerId,
            Type = (int)desired.Type,
            Value = desired.Value,
            NormalizedValue = desired.NormalizedValue,
            Revision = now.UtcDateTime.Ticks,
            UpdatedAt = now
        };
    }

    private sealed record DesiredIdentifier(
        int CustomerId,
        CustomerIdentifierType Type,
        string Value,
        string NormalizedValue);
}
