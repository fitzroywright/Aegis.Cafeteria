namespace Aegis.Cafeteria.PersistenceTests;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Aegis.Cafeteria.Infrastructure.Ffp;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

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
        FfpSqlApplicationGateway gateway = new(options, NullLogger<FfpSqlApplicationGateway>.Instance);

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

    [Theory]
    [InlineData("NFC", "04A1B2C3")]
    [InlineData("Code", "EMP-IDMETHOD")]
    [InlineData("TRN", "123456789")]
    [InlineData("Name", "NAME SEARCH")]
    public async Task LiveFfp_Purchase_WritesRequiredIDMethodMapping(string scanCode, string identificationCode)
    {
        if (!LiveWriteEnabled()) return;

        int employeeId = TestEmployeeId();
        string connectionString = ConnectionString();
        Guid transmissionId = Guid.NewGuid();
        decimal creditLimit;
        decimal availableBalance;

        await using (SqlConnection connection = new(connectionString))
        {
            await connection.OpenAsync();
            await using SqlCommand current = new(
                "SELECT ISNULL(CreditLimit,0), ISNULL(AvailableBalance,0) FROM dbo.Employees WHERE ID=@EmployeeID AND EligibleForLunchTicket=1;",
                connection);
            current.Parameters.AddWithValue("@EmployeeID", employeeId);
            await using SqlDataReader reader = await current.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), $"Configured FFP live-test employee {employeeId} was not found or is not cafeteria eligible.");
            creditLimit = Convert.ToDecimal(reader.GetValue(0));
            availableBalance = Convert.ToDecimal(reader.GetValue(1));
        }

        FfpIntegrationOptions options = Options(allowWrites: true);
        Assert.Equal(8, options.PurchaseScanType);
        FfpSqlApplicationGateway gateway = new(options, NullLogger<FfpSqlApplicationGateway>.Instance);

        try
        {
            ExternalLedgerEntry entry = new(
                transmissionId,
                employeeId,
                ExternalLedgerEntryKind.Purchase,
                DateTimeOffset.Now,
                "IDMethod live integration verification",
                0m,
                creditLimit,
                availableBalance,
                scanCode,
                "IDMethod Test Cashier",
                "Cafeteria Purchase",
                identificationCode,
                "SYSTEM");

            await gateway.PublishLedgerEntryAsync(entry);

            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync();
            const string sql = @"
SELECT Code, ScanCode, UserName, CreatedBy, ScanType
FROM dbo.EmployeeIDScans
WHERE TransmissionID = @TransmissionID;";
            await using SqlCommand command = new(sql, connection);
            command.Parameters.AddWithValue("@TransmissionID", transmissionId.ToString());
            await using SqlDataReader reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(identificationCode, Convert.ToString(reader.GetValue(0))?.Trim());
            Assert.Equal(scanCode, Convert.ToString(reader.GetValue(1))?.Trim());
            Assert.Equal("IDMethod Test Cashier", Convert.ToString(reader.GetValue(2))?.Trim());
            Assert.Equal("SYSTEM", Convert.ToString(reader.GetValue(3))?.Trim());
            Assert.Equal(8, Convert.ToInt32(reader.GetValue(4)));
        }
        finally
        {
            await using SqlConnection cleanupConnection = new(connectionString);
            await cleanupConnection.OpenAsync();
            await using SqlCommand cleanup = new(
                "DELETE FROM dbo.EmployeeIDScans WHERE TransmissionID=@TransmissionID;",
                cleanupConnection);
            cleanup.Parameters.AddWithValue("@TransmissionID", transmissionId.ToString());
            await cleanup.ExecuteNonQueryAsync();
        }
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
