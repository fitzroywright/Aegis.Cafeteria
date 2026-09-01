namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Domain.Identification;
using Aegis.Cafeteria.Infrastructure.ActiveDirectory;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Persistence;
using Aegis.Cafeteria.Infrastructure.Persistence.Models;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

public sealed class EfFfpReferenceCache(
    IDbContextFactory<LocalCafeteriaDbContext> factory,
    FfpIntegrationOptions? ffpOptions = null,
    ILogger<EfFfpReferenceCache>? logger = null) : IWorkforceReferenceCache
{
    private readonly ActiveDirectoryEmployeeDirectory directory = new();

    public async Task ReplaceCustomersAsync(IReadOnlyCollection<ExternalCustomerSnapshot> employees, CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<int, CustomerDirectoryMetadata> directoryMetadata = await LoadDirectoryMetadataAsync(employees, cancellationToken);

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

            directoryMetadata.TryGetValue(employee.CustomerId, out CustomerDirectoryMetadata? metadata);

            row.Name = employee.Name;
            row.NormalizedName = DomainPersistenceMapper.NormalizeSearchText(employee.Name);
            row.LoginName = metadata?.LoginName ?? string.Empty;
            row.Department = metadata?.Department ?? string.Empty;
            row.Manager = metadata?.Manager ?? string.Empty;
            row.EmailAddress = metadata?.EmailAddress ?? string.Empty;
            row.Extension = metadata?.Extension ?? string.Empty;
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

    private async Task<IReadOnlyDictionary<int, CustomerDirectoryMetadata>> LoadDirectoryMetadataAsync(
        IReadOnlyCollection<ExternalCustomerSnapshot> employees,
        CancellationToken cancellationToken)
    {
        string connectionString = ffpOptions?.ConnectionString ?? string.Empty;
        if (employees.Count == 0 || string.IsNullOrWhiteSpace(connectionString))
        {
            return new Dictionary<int, CustomerDirectoryMetadata>();
        }

        try
        {
            string employeeIds = string.Join(",", employees.Select(employee => employee.CustomerId).Distinct().OrderBy(employeeId => employeeId));
            string sql = $@"
SELECT
      e.ID
    , ISNULL(CONVERT(nvarchar(256), e.LoginName), '') AS LoginName
FROM dbo.Employees AS e
WHERE e.ID IN ({employeeIds});";

            Dictionary<int, string> loginNames = [];
            await using (SqlConnection connection = new(connectionString))
            {
                await connection.OpenAsync(cancellationToken);
                await using SqlCommand command = new(sql, connection);
                await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    int employeeId = Convert.ToInt32(reader.GetValue(0));
                    string loginName = ReadString(reader, 1);
                    loginNames[employeeId] = loginName;
                }
            }

            IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> adRecords = directory.GetByLoginNames(loginNames.Values);
            Dictionary<int, CustomerDirectoryMetadata> result = [];

            foreach (KeyValuePair<int, string> employee in loginNames)
            {
                string normalizedLogin = NormalizeLoginName(employee.Value);
                if (normalizedLogin.Length == 0 || !adRecords.TryGetValue(normalizedLogin, out ActiveDirectoryEmployeeRecord? adEmployee))
                {
                    result[employee.Key] = new CustomerDirectoryMetadata(employee.Value, string.Empty, string.Empty, string.Empty, string.Empty);
                    continue;
                }

                result[employee.Key] = new CustomerDirectoryMetadata(
                    employee.Value,
                    adEmployee.Department,
                    adEmployee.Manager,
                    adEmployee.EmailAddress,
                    adEmployee.Extension);
            }

            return result;
        }
        catch (Exception exception) when (exception is SqlException or InvalidOperationException)
        {
            logger?.LogWarning(
                exception,
                "Could not enrich the Cafeteria employee cache from FFP LoginName and Active Directory metadata");
            return new Dictionary<int, CustomerDirectoryMetadata>();
        }
    }

    private static string ReadString(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? string.Empty : Convert.ToString(reader.GetValue(ordinal))?.Trim() ?? string.Empty;

    private static string NormalizeLoginName(string value)
    {
        string loginName = value.Trim();
        int slash = loginName.LastIndexOf('\\');
        if (slash >= 0 && slash < loginName.Length - 1)
        {
            loginName = loginName[(slash + 1)..];
        }

        int at = loginName.IndexOf('@');
        if (at > 0)
        {
            loginName = loginName[..at];
        }

        return loginName.Trim().ToLowerInvariant();
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

    private sealed record CustomerDirectoryMetadata(
        string LoginName,
        string Department,
        string Manager,
        string EmailAddress,
        string Extension);
}
