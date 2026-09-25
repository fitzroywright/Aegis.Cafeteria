namespace Aegis.Cafeteria.PersistenceTests;

using Microsoft.Data.SqlClient;

public sealed class FfpManagerDatabaseContractTests
{
    private static string BuildTestConnectionString()
    {
        SqlConnectionStringBuilder builder = new(ConnectionString!);
        builder.TrustServerCertificate = true;
        return builder.ConnectionString;
    }

    private static string? ConnectionString =>
        Environment.GetEnvironmentVariable("FFPMANAGER_TEST");

    [Fact]
    public async Task FfpManager_test_database_contains_required_cafeteria_contract()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using SqlConnection connection = new(BuildTestConnectionString());
        await connection.OpenAsync();

        Dictionary<string, string[]> required = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Employees"] =
            [
                "ID",
                "IsActive",
                "NFCID",
                "RFID",
                "Code",
                "TRN",
                "Name"
            ],
            ["Holidays"] =
            [
                "CalendarDate"
            ],
            ["LunchItems"] =
            [
                "ID",
                "Code",
                "Name",
                "Price",
                "Cost"
            ],
            ["EmployeeIDScans"] =
            [
                "TransmissionID",
                "EmployeeID",
                "ScanDate",
                "ScanType",
                "CreatedBy"
            ]
        };

        List<string> missing = [];

        foreach ((string table, string[] columns) in required)
        {
            foreach (string column in columns)
            {
                await using SqlCommand command = new(
                    """
                    SELECT COUNT(*)
                    FROM sys.tables t
                    JOIN sys.columns c ON c.object_id = t.object_id
                    WHERE t.name = @table AND c.name = @column;
                    """,
                    connection);

                command.Parameters.AddWithValue("@table", table);
                command.Parameters.AddWithValue("@column", column);

                if (Convert.ToInt32(await command.ExecuteScalarAsync()) == 0)
                {
                    missing.Add($"{table}.{column}");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            "FFPMANAGER_TEST is missing Cafeteria contract members: " +
            string.Join(", ", missing));
    }

    [Fact]
    public async Task FfpManager_test_database_has_FFP00_policy_row()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using SqlConnection connection = new(BuildTestConnectionString());
        await connection.OpenAsync();

        await using SqlCommand command = new(
            """
            SELECT TOP 1 Price, Cost
            FROM LunchItems
            WHERE UPPER(Code) = 'FFP00';
            """,
            connection);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();

        Assert.True(
            await reader.ReadAsync(),
            "FFPMANAGER_TEST must contain the FFP00 LunchItems policy row.");

        decimal price = Convert.ToDecimal(reader.GetValue(0));
        decimal cost = Convert.ToDecimal(reader.GetValue(1));

        Assert.True(price >= 0m, "FFP00 Price cannot be negative.");
        Assert.True(cost >= 0m, "FFP00 Cost cannot be negative.");
    }

    [Fact]
    public async Task FfpManager_test_database_has_employee_reference_data()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString))
        {
            return;
        }

        await using SqlConnection connection = new(BuildTestConnectionString());
        await connection.OpenAsync();

        await using SqlCommand command = new(
            """
            SELECT COUNT(*)
            FROM Employees
            WHERE ID IS NOT NULL;
            """,
            connection);

        int count = Convert.ToInt32(await command.ExecuteScalarAsync());

        Assert.True(
            count > 0,
            "FFPMANAGER_TEST requires at least one employee for Cafeteria integration testing.");
    }
}
