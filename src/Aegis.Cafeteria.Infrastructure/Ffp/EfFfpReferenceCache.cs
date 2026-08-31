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
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var allCustomers = await db.Customers.ToListAsync(cancellationToken);
        var allIdentifiers = await db.CustomerIdentifiers.ToListAsync(cancellationToken);

        // Employee identifiers are entirely FFP-owned. Rebuild the local identifier projection as one set so
        // reassigned cards cannot collide with a stale row belonging to the previous employee.
        db.CustomerIdentifiers.RemoveRange(allIdentifiers);
        var sourceIds = employees.Select(x => x.CustomerId).ToHashSet();
        foreach (var stale in allCustomers.Where(x => !sourceIds.Contains(x.Id)))
        {
            // Preserve the historical customer row for old orders, but never leave a missing FFP employee eligible.
            stale.IsEligibleForCredit = false;
            stale.Revision = now.UtcDateTime.Ticks;
            stale.UpdatedAt = now;
        }

        var claimedIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var employee in employees)
        {
            var row = allCustomers.SingleOrDefault(x => x.Id == employee.CustomerId);
            if (row is null)
            {
                row = new CustomerRow { Id = employee.CustomerId };
                db.Customers.Add(row);
                allCustomers.Add(row);
            }
            row.Name = employee.Name;
            row.NormalizedName = DomainPersistenceMapper.NormalizeSearchText(employee.Name);
            row.Department = employee.Department;
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
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var allHolidays = await db.Holidays.ToListAsync(cancellationToken);
        var existing = allHolidays.Where(x => x.Date.Year == year && x.Date.Month == month).ToList();
        db.Holidays.RemoveRange(existing);
        var now = DateTimeOffset.UtcNow;
        foreach (var d in holidays.Distinct()) db.Holidays.Add(new HolidayRow { Date = d, Name = "FFP Holiday", Revision = now.UtcDateTime.Ticks, UpdatedAt = now });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddIdentifier(LocalCafeteriaDbContext db, HashSet<string> claimed, int customerId, CustomerIdentifierType type, string? value, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var normalized = DomainPersistenceMapper.NormalizeIdentifier(type, value);
        if (string.IsNullOrWhiteSpace(normalized)) return;
        var claim = $"{(int)type}:{normalized}";
        if (!claimed.Add(claim)) return;
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"FFP:{customerId}:{type}:{normalized}"));
        db.CustomerIdentifiers.Add(new CustomerIdentifierRow
        {
            Id = new Guid(bytes.AsSpan(0, 16)), CustomerId = customerId, Type = (int)type,
            Value = value.Trim(), NormalizedValue = normalized, Revision = now.UtcDateTime.Ticks, UpdatedAt = now
        });
    }
}
