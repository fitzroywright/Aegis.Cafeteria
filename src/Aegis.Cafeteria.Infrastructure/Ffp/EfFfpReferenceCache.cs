namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class EfFfpReferenceCache(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    FfpIntegrationOptions ffpOptions,
    ILogger<EfFfpReferenceCache> logger) : IWorkforceReferenceCache
{
    public async Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> employees, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, CustomerDirectoryMetadata> directory = await LoadDirectoryMetadataAsync(employees, cancellationToken);

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

            directory.TryGetValue(employee.CustomerId, out CustomerDirectoryMetadata? metadata);

            row.Name = employee.Name;
            row.NormalizedName = DomainPersistenceMapper.NormalizeSearchText(employee.Name);
            row.Department = employee.Department;
            row.Manager = FirstNonBlank(metadata?.Manager, employee.Manager);
            row.EmailAddress = FirstNonBlank(metadata?.EmailAddress, employee.EmailAddress);
            row.Extension = FirstNonBlank(metadata?.Extension, employee.Extension);
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

    private async Task<IReadOnlyDictionary<int, CustomerDirectoryMetadata>> LoadDirectoryMetadataAsync(
        IReadOnlyCollection<ExternalCustomerSnapshot> employees,
        CancellationToken cancellationToken)
    {
        if (employees.Count == 0 || string.IsNullOrWhiteSpace(ffpOptions.ConnectionString))
        {
            return new Dictionary<int, CustomerDirectoryMetadata>();
        }

        try
        {
            await using SqlConnection connection = new(ffpOptions.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            HashSet<string> columns = await ReadEmployeeColumnsAsync(connection, cancellationToken);
            string emailColumn = FirstExistingColumn(columns, "Email", "EmailAddress", "EMail") ?? string.Empty;
            string extensionColumn = FirstExistingColumn(columns, "Extension", "Ext", "PhoneExtension", "TelephoneExtension") ?? string.Empty;
            string? managerTextColumn = FirstExistingColumn(columns, "Manager", "ManagerName", "Supervisor");
            string? managerIdColumn = FirstExistingColumn(columns, "ManagerID", "SupervisorID", "ReportsToID");

            string managerExpression;
            string managerJoin;
            if (!string.IsNullOrWhiteSpace(managerTextColumn))
            {
                managerExpression = $"ISNULL(CONVERT(nvarchar(200), e.[{managerTextColumn}]), '')";
                managerJoin = string.Empty;
            }
            else if (!string.IsNullOrWhiteSpace(managerIdColumn))
            {
                managerExpression = "LTRIM(RTRIM(ISNULL(m.FirstName, '') + ' ' + ISNULL(m.LastName, '')))";
                managerJoin = $" LEFT OUTER JOIN dbo.Employees AS m ON m.ID = e.[{managerIdColumn}]";
            }
            else
            {
                managerExpression = "CAST('' AS nvarchar(200))";
                managerJoin = string.Empty;
            }

            string emailExpression = string.IsNullOrWhiteSpace(emailColumn)
                ? "CAST('' AS nvarchar(320))"
                : $"ISNULL(CONVERT(nvarchar(320), e.[{emailColumn}]), '')";
            string extensionExpression = string.IsNullOrWhiteSpace(extensionColumn)
                ? "CAST('' AS nvarchar(32))"
                : $"ISNULL(CONVERT(nvarchar(32), e.[{extensionColumn}]), '')";

            string employeeIds = string.Join(",", employees.Select(x => x.CustomerId).Distinct().OrderBy(x => x));
            string sql = $@"
SELECT
      e.ID
    , {managerExpression} AS ManagerName
    , {emailExpression} AS EmailAddress
    , {extensionExpression} AS Extension
FROM dbo.Employees AS e
{managerJoin}
WHERE e.ID IN ({employeeIds});";

            Dictionary<int, CustomerDirectoryMetadata> result = [];
            await using SqlCommand command = new(sql, connection);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                int employeeId = Convert.ToInt32(reader.GetValue(0));
                result[employeeId] = new CustomerDirectoryMetadata
                (
                    ReadString(reader, 1),
                    ReadString(reader, 2),
                    ReadString(reader, 3)
                );
            }

            return result;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            // Directory metadata is supplementary. Never block the eligible employee cache or POS startup
            // if an older FFP schema does not expose one of these optional directory fields.
            logger.LogWarning(ex, "Could not enrich the Cafeteria employee cache with FFP manager/email/extension metadata");
            return new Dictionary<int, CustomerDirectoryMetadata>();
        }
    }

    private static async Task<HashSet<string>> ReadEmployeeColumnsAsync(SqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = @"
SELECT c.name
FROM sys.columns AS c
WHERE c.object_id = OBJECT_ID(N'dbo.Employees');";

        HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
        await using SqlCommand command = new(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(reader.GetString(0));
        }
        return result;
    }

    private static string? FirstExistingColumn(HashSet<string> columns, params string[] candidates)
        => candidates.FirstOrDefault(columns.Contains);

    private static string ReadString(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? string.Empty;

    private static string FirstNonBlank(string? preferred, string fallback)
        => string.IsNullOrWhiteSpace(preferred) ? fallback : preferred.Trim();

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

    private sealed record CustomerDirectoryMetadata(string Manager, string EmailAddress, string Extension);
}
