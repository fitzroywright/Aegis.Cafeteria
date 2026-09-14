namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.ActiveDirectory;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

public sealed class FfpEmployeeWorkforceSource(FfpIntegrationOptions options) : IWorkforceReferenceSource
{
    private readonly ActiveDirectoryEmployeeDirectory directory = new();

    private const string EmployeeSql = @"
SELECT
      e.ID
    , e.Code
    , e.TRN
    , e.NFCID
    , ISNULL(e.FirstName, '') + ' ' + ISNULL(e.LastName, '') AS Name
    , e.Photo
    , CAST(CASE WHEN e.EligibleForLunchTicket = 1 THEN 1 ELSE 0 END AS bit) AS IsEligible
    , ISNULL(CONVERT(nvarchar(256), e.LoginName), '') AS LoginName
    , CAST(CASE WHEN ISNULL(e.IsActive, 0) = 1 THEN 1 ELSE 0 END AS bit) AS IsActive
    , e.DateEmployed
FROM dbo.Employees AS e
WHERE e.EligibleForLunchTicket = 1;";

    private const string HolidaySql = @"
SELECT Name, CalendarDate
FROM dbo.Holidays
WHERE YEAR(CalendarDate) = @year;";

    public async Task<IReadOnlyList<ExternalCustomerSnapshot>> GetCustomersAsync(CancellationToken cancellationToken = default)
    {
        EnsureEnabled();

        List<EmployeeSourceRow> rows = [];
        await using (SqlConnection connection = new(options.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using SqlCommand command = new(EmployeeSql, connection);
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add
                (
                    new EmployeeSourceRow
                    {
                          CustomerId = Convert.ToInt32(reader.GetValue(0))
                        , Code = NormalizeIdentifier(reader, 1)
                        , Trn = NormalizeIdentifier(reader, 2)
                        , Nfc = NormalizeIdentifier(reader, 3)
                        , Name = Convert.ToString(reader.GetValue(4))?.Trim() ?? string.Empty
                        , Photo = reader.IsDBNull(5) ? null : (byte[])reader.GetValue(5)
                        , LunchEligible = !reader.IsDBNull(6) && Convert.ToBoolean(reader.GetValue(6))
                        , LoginName = reader.IsDBNull(7) ? string.Empty : Convert.ToString(reader.GetValue(7))?.Trim() ?? string.Empty
                        , IsActive = !reader.IsDBNull(8) && Convert.ToBoolean(reader.GetValue(8))
                        , EmploymentStartDate = reader.IsDBNull(9)
                            ? null
                            : DateOnly.FromDateTime(Convert.ToDateTime(reader.GetValue(9)))
                    }
                );
            }
        }

        IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> directoryRecords;
        try
        {
            directoryRecords = this.directory.GetByLoginNames(rows.Select(row => row.LoginName));
        }
        catch
        {
            directoryRecords = new Dictionary<string, ActiveDirectoryEmployeeRecord>(StringComparer.OrdinalIgnoreCase);
        }

        return rows.Select(row =>
        {
            ActiveDirectoryEmployeeRecord? directoryEmployee = FindDirectoryRecord(directoryRecords, row.LoginName);
            return new ExternalCustomerSnapshot
            {
                  CustomerId = row.CustomerId
                , Code = row.Code
                , Trn = row.Trn
                , Nfc = row.Nfc
                , Name = row.Name
                , Photo = row.Photo
                , IsActive = row.IsActive
                , EmploymentStartDate = row.EmploymentStartDate
                , IsEligibleForCafeteriaCredit = row.LunchEligible && row.IsActive
                , LoginName = row.LoginName
                , Department = directoryEmployee?.Department ?? string.Empty
                , Manager = directoryEmployee?.Manager ?? string.Empty
                , EmailAddress = directoryEmployee?.EmailAddress ?? string.Empty
                , Extension = directoryEmployee?.Extension ?? string.Empty
                , AdditionalCreditLimit = 0m
            };
        }).ToArray();
    }

    public async Task<IReadOnlyCollection<DateOnly>> GetHolidaysAsync(int year, int month, CancellationToken cancellationToken = default)
    {
        EnsureEnabled();
        if (year < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }
        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        List<DateOnly> dates = [];
        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(HolidaySql, connection);
        command.Parameters.AddWithValue("@year", year);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            DateOnly date = DateOnly.FromDateTime(reader.GetDateTime(1));
            if (date.Month == month)
            {
                dates.Add(date);
            }
        }

        return dates.Distinct().ToArray();
    }

    private static ActiveDirectoryEmployeeRecord? FindDirectoryRecord(
        IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> records,
        string loginName)
    {
        string normalized = NormalizeLoginName(loginName);
        return records.TryGetValue(normalized, out ActiveDirectoryEmployeeRecord? record) ? record : null;
    }

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

    private static string? NormalizeIdentifier(SqlDataReader reader, int ordinal)
    {
        if (reader.IsDBNull(ordinal))
        {
            return null;
        }

        string? value = Convert.ToString(reader.GetValue(ordinal))?.Trim();
        return string.IsNullOrEmpty(value) ? null : value.ToLowerInvariant();
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled)
        {
            throw new InvalidOperationException("FFP integration is disabled.");
        }
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("FFP database connection string is not configured.");
        }
    }

    private sealed record EmployeeSourceRow
    {
        public int CustomerId { get; init; }
        public string? Code { get; init; }
        public string? Trn { get; init; }
        public string? Nfc { get; init; }
        public string Name { get; init; } = string.Empty;
        public byte[]? Photo { get; init; }
        public bool LunchEligible { get; init; }
        public string LoginName { get; init; } = string.Empty;
        public bool IsActive { get; init; }
        public DateOnly? EmploymentStartDate { get; init; }
    }
}
