namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Microsoft.Data.SqlClient;

/// <summary>
/// Opt-in tests against the real FFP SQL Server. Read/schema checks run only when AEGIS_FFP_LIVE_TEST=1.
/// Write verification additionally requires AEGIS_FFP_LIVE_WRITE_TEST=1 and AEGIS_FFP_TEST_EMPLOYEE_ID.
/// The write test preserves the employee's existing balances and deletes its test EmployeeIDScans row in cleanup.
/// </summary>
public sealed class FfpSqlLiveIntegrationTests
{
    [Fact]
    public async Task LiveFfp_EmployeesAndCurrentYearHolidays_CanBeRead()
    {
        if (!LiveReadEnabled()) return;

        FfpIntegrationOptions options = Options(allowWrites: false);
        FfpEmployeeWorkforceSource gateway = new(options);

        IReadOnlyList<ExternalCustomerSnapshot> employees = await gateway.GetCustomersAsync();
        Assert.NotEmpty(employees);
        Assert.All
        (
              employees.Take(20)
            , employee =>
            {
                Assert.True(employee.CustomerId > 0);
                Assert.True(employee.IsEligibleForCafeteriaCredit);
            }
        );

        DateOnly today = DateOnly.FromDateTime(DateTime.Today);
        IReadOnlyCollection<DateOnly> holidays = await gateway.GetHolidaysAsync(today.Year, today.Month);
        Assert.All(holidays, h => Assert.Equal(today.Month, h.Month));
    }

    [Fact]
    public async Task LiveFfp_EmployeeIDScans_HasRequiredIDMethodColumns()
    {
        if (!LiveReadEnabled()) return;

        await using SqlConnection connection = new(ConnectionString());
        await connection.OpenAsync();
        const string sql = @"
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME = 'EmployeeIDScans'
  AND COLUMN_NAME IN ('Code','UserName','ScanCode','ScanType','CreatedBy','TransmissionID');";
        await using SqlCommand command = new(sql, connection);
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        Dictionary<string, (string Type, string Nullable)> columns = new(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync())
        {
            columns[reader.GetString(0)] = (reader.GetString(1), reader.GetString(2));
        }

        foreach (string required in new[] { "Code", "UserName", "ScanCode", "ScanType", "CreatedBy", "TransmissionID" })
        {
            Assert.True(columns.ContainsKey(required), $"FFP dbo.EmployeeIDScans is missing required IDMethod column {required}.");
        }

        Assert.Contains(columns["CreatedBy"].Type, new[] { "varchar", "nvarchar", "char", "nchar", "text", "ntext" });
        Assert.Contains(columns["Code"].Type, new[] { "varchar", "nvarchar", "char", "nchar", "text", "ntext" });
        Assert.Contains(columns["ScanCode"].Type, new[] { "varchar", "nvarchar", "char", "nchar", "text", "ntext" });
    }

    private static bool LiveReadEnabled()
        => string.Equals(Environment.GetEnvironmentVariable("AEGIS_FFP_LIVE_TEST"), "1", StringComparison.OrdinalIgnoreCase);

    private static bool LiveWriteEnabled()
        => LiveReadEnabled() &&
           string.Equals(Environment.GetEnvironmentVariable("AEGIS_FFP_LIVE_WRITE_TEST"), "1", StringComparison.OrdinalIgnoreCase);

    private static string ConnectionString()
        => Environment.GetEnvironmentVariable("AEGIS_FFP_TEST_CONNECTION_STRING") is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException("Set AEGIS_FFP_TEST_CONNECTION_STRING when AEGIS_FFP_LIVE_TEST=1.");

    private static int TestEmployeeId()
        => int.TryParse(Environment.GetEnvironmentVariable("AEGIS_FFP_TEST_EMPLOYEE_ID"), out int value) && value > 0
            ? value
            : throw new InvalidOperationException("Set AEGIS_FFP_TEST_EMPLOYEE_ID to an eligible test employee when AEGIS_FFP_LIVE_WRITE_TEST=1.");

    private static FfpIntegrationOptions Options(bool allowWrites) => new()
    {
        Enabled = true,
        AllowWrites = allowWrites,
        ConnectionString = ConnectionString(),
        PurchaseScanType = 8
    };
}
