namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.EntityFrameworkCore;

public sealed class EfFfpReferenceCache(
    IDbContextFactory<LocalCafeteriaDbContext> factory) : IWorkforceReferenceCache
{
    public async Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> employees, CancellationToken cancellationToken = default)
    {
        await using LocalCafeteriaDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction tx = await db.Database.BeginTransactionAsync(cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        List<CustomerRow> allCustomers = await db.Customers.ToListAsync(cancellationToken);
        List<CustomerIdentifierRow> allIdentifiers = await db.CustomerIdentifiers.ToListAsync(cancellationToken);

        db.CustomerIdentifiers.RemoveRange(allIdentifiers);
        HashSet<int> sourceIds = employees.Select(employee => employee.CustomerId).ToHashSet();
        foreach (CustomerRow stale in allCustomers.Where(customer => !sourceIds.Contains(customer.Id)))
        {
            stale.IsEligibleForCredit = false;
            stale.Revision = now.UtcDateTime.Ticks;
            stale.UpdatedAt = now;
        }

        HashSet<string> claimedIdentifiers = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExternalCustomerSnapshot employee in employees)
        {
            CustomerRow? row = allCustomers.SingleOrDefault(customer => customer.Id == employee.CustomerId);
            if (row is null)
            {
                row = new CustomerRow { Id = employee.CustomerId };
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
        List<HolidayRow> existing = allHolidays.Where(holiday => holiday.Date.Year == year && holiday.Date.Month == month).ToList();
        db.Holidays.RemoveRange(existing);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        foreach (DateOnly holiday in holidays.Distinct())
        {
            db.Holidays.Add(new HolidayRow
            {
                Date = holiday,
                Name = "FFP Holiday",
                Revision = now.UtcDateTime.Ticks,
                UpdatedAt = now
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddIdentifier(LocalCafeteriaDbContext db, HashSet<string> claimed, int customerId, CustomerIdentifierType type, string? value, DateTimeOffset now)
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

        string claim = $"{(int)type}:{normalized}";
        if (!claimed.Add(claim))
        {
            return;
        }

        byte[] bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"FFP:{customerId}:{type}:{normalized}"));
        db.CustomerIdentifiers.Add(new CustomerIdentifierRow
        {
            Id = new Guid(bytes.AsSpan(0, 16)),
            CustomerId = customerId,
            Type = (int)type,
            Value = value.Trim(),
            NormalizedValue = normalized,
            Revision = now.UtcDateTime.Ticks,
            UpdatedAt = now
        });
    }
}
