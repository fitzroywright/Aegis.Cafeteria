namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfFfpReferenceCache(IDbContextFactory<LocalCafeteriaDbContext> factory) : IWorkforceReferenceCache
{
    public async Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> employees, CancellationToken cancellationToken = default)
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<CustomerRow> allCustomers = await db.Customers.ToListAsync(cancellationToken);
        List<CustomerIdentifierRow> allIdentifiers = await db.CustomerIdentifiers.ToListAsync(cancellationToken);

        // Employee identifiers are entirely FFP-owned. Rebuild the local identifier projection as one set so
        // reassigned cards cannot collide with a stale row belonging to the previous employee.
        db.CustomerIdentifiers.RemoveRange(allIdentifiers);
        HashSet<int> sourceIds = employees.Select(x => x.CustomerId).ToHashSet();
        foreach (CustomerRow stale in allCustomers.Where(x => !sourceIds.Contains(x.Id)))
        {
            // Preserve the historical customer row for old orders, but never leave a missing FFP employee eligible.
            stale.IsEligibleForCredit = false;
            stale.Revision = now.UtcDateTime.Ticks;
            stale.UpdatedAt = now;
        }

        HashSet<string> claimedIdentifiers = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExternalCustomerSnapshot employee in employees)
        {
            CustomerRow? row = allCustomers.SingleOrDefault(x => x.Id == employee.CustomerId);
            if (row is null)
            {
                row = new CustomerRow { Id = employee.CustomerId };
                db.Customers.Add(row);
                allCustomers.Add(row);
            }
            row.Name = employee.Name;
            row.NormalizedName = DomainPersistenceMapper.NormalizeSearchText(employee.Name);
            row.Department = employee.Department;
            row.Manager = employee.Manager;
            row.EmailAddress = employee.EmailAddress;
            row.Extension = employee.Extension;
            row.Photo = employee.Photo;
            row.IsEligibleForCredit = employee.IsEligibleForCafeteriaCredit;
            row.AdditionalCreditLimit = employee.AdditionalCreditLimit;
            row.Revision = now.UtcDateTime.Ticks;
            row.UpdatedAt = now;

            AddIdentifier(db, claimedIdentifiers, employee.CustomerId, CustomerIdentifierType.NfcTag, employee.Nfc, now);
            AddIdentifier(db, claimedIdentifiers, employee.CustomerId, CustomerIdentifierType.Code, employee.Code, now);
            AddIdentifier(db, claimedIdentifiers, employee.CustomerId, CustomerIdentifierType.Trn, employee.Trn, now);
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task ReplaceHolidaysAsync(int year, int month, IReadOnlyCollection<DateOnly> holidays, CancellationToken cancellationToken = default)
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        List<HolidayRow> allHolidays = await db.Holidays.ToListAsync(cancellationToken);
        List<HolidayRow> existing = allHolidays.Where(x => x.Date.Year == year && x.Date.Month == month).ToList();
        db.Holidays.RemoveRange(existing);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        foreach (DateOnly d in holidays.Distinct()) db.Holidays.Add(new HolidayRow { Date = d, Name = "FFP Holiday", Revision = now.UtcDateTime.Ticks, UpdatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddIdentifier(LocalCafeteriaDbContext db, HashSet<string> claimed, int customerId, CustomerIdentifierType type, string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        string normalized = DomainPersistenceMapper.NormalizeIdentifier(type, value);
        if (string.IsNullOrWhiteSpace(normalized)) return;
        string claim = $"{(int)type}:{normalized}";
        if (!claimed.Add(claim)) return;
        byte[] bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"FFP:{customerId}:{type}:{normalized}"));
        db.CustomerIdentifiers.Add(new CustomerIdentifierRow
        {
            Id = new Guid(bytes.AsSpan(0, 16)), CustomerId = customerId, Type = (int)type,
            Value = value.Trim(), NormalizedValue = normalized, Revision = now.UtcDateTime.Ticks, UpdatedAt = now
        });
    }
}
