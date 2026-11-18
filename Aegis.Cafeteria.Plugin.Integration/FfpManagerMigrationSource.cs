namespace Aegis.Cafeteria.Plugin.Integration;

using Aegis.Cafeteria.Application.Integration;
using Microsoft.Data.SqlClient;

public sealed class FfpManagerMigrationSource : ILegacyCafeteriaMigrationSource
{
    public async Task<IReadOnlyList<LegacyCafeteriaProductSnapshot>> ReadProductsAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        const string sql = """
            SELECT
                  ID
                , ISNULL(CONVERT(nvarchar(64), Code), '') AS Code
                , ISNULL(CONVERT(nvarchar(256), Name), '') AS Name
                , ISNULL(Price, 0) AS Price
            FROM dbo.LunchItems
            CROSS APPLY (VALUES (ISNULL(CONVERT(nvarchar(max), Code), N'') COLLATE Latin1_General_100_BIN2)) AS source(CodeText)
            WHERE SUBSTRING(source.CodeText, PATINDEX(@firstNonWhitespace, source.CodeText), 3)
                NOT LIKE N'[Ff][Ff][Pp]'
            ORDER BY ID;
            """;

        List<LegacyCafeteriaProductSnapshot> products = [];

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using SqlCommand command = new(sql, connection);
        // Match the first character that .NET Trim would leave in the code.
        const string whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680"
            + "\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
        command.Parameters.AddWithValue("@firstNonWhitespace", $"%[^{whitespace}]%");
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            int productId = Convert.ToInt32(reader.GetValue(0));
            string code = Convert.ToString(reader.GetValue(1))?.Trim() ?? string.Empty;
            if (LunchItemCode.IsReserved(code))
                continue;

            string name = Convert.ToString(reader.GetValue(2))?.Trim() ?? string.Empty;
            decimal price = Convert.ToDecimal(reader.GetValue(3));

            if (productId <= 0 || string.IsNullOrWhiteSpace(name) || price < 0m)
                continue;

            products.Add(new LegacyCafeteriaProductSnapshot(productId, code, name, price));
        }

        return products;
    }

    public async Task<LegacyCafeteriaMonthSnapshot> ReadMonthAsync(
        string connectionString,
        int year,
        int month,
        DateTimeOffset throughAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        if (year is < 2000 or > 9999) throw new ArgumentOutOfRangeException(nameof(year));
        if (month is < 1 or > 12) throw new ArgumentOutOfRangeException(nameof(month));

        DateTime start = new(year, month, 1);
        DateTime end = start.AddMonths(1);

        const string transactionSql = """
            SELECT
                  s.ID
                , s.EmployeeID
                , s.ScanDate
                , ISNULL(s.IDName, '')
                , ISNULL(s.[Description], '')
                , ISNULL(s.ScanValue, 0)
                , ISNULL(s.UserName, '')
                , ISNULL(s.ScanCode, '')
                , ISNULL(s.ScanType, 0)
            FROM dbo.EmployeeIDScans AS s
            INNER JOIN dbo.Employees AS e ON e.ID = s.EmployeeID
            WHERE s.ScanDate >= @Start
              AND s.ScanDate < @End
              AND s.ScanDate <= @ThroughAt
              AND e.EligibleForLunchTicket = 1
              AND s.ScanType IN (3, 8, 4)
            ORDER BY s.ScanDate, s.ID;
            """;

        const string accountSql = """
            SELECT
                  e.ID
                , ISNULL(e.CreditLimit, 0)
                , ISNULL(e.AvailableBalance, 0)
            FROM dbo.Employees AS e
            WHERE e.EligibleForLunchTicket = 1
            ORDER BY e.ID;
            """;

        List<LegacyCafeteriaTransactionSnapshot> transactions = [];
        List<LegacyCafeteriaAccountSnapshot> accounts = [];

        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        await using (SqlCommand command = new(transactionSql, connection))
        {
            command.Parameters.AddWithValue("@Start", start);
            command.Parameters.AddWithValue("@End", end);
            command.Parameters.AddWithValue("@ThroughAt", throughAt.LocalDateTime);

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                transactions.Add(new LegacyCafeteriaTransactionSnapshot(
                    Convert.ToInt64(reader.GetValue(0)),
                    Convert.ToInt32(reader.GetValue(1)),
                    new DateTimeOffset(reader.GetDateTime(2)),
                    Convert.ToString(reader.GetValue(3))?.Trim() ?? string.Empty,
                    Convert.ToString(reader.GetValue(4))?.Trim() ?? string.Empty,
                    Convert.ToDecimal(reader.GetValue(5)),
                    Convert.ToString(reader.GetValue(6))?.Trim() ?? string.Empty,
                    Convert.ToString(reader.GetValue(7))?.Trim() ?? string.Empty,
                    Convert.ToInt32(reader.GetValue(8))));
            }
        }

        await using (SqlCommand command = new(accountSql, connection))
        {
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                accounts.Add(new LegacyCafeteriaAccountSnapshot(
                    Convert.ToInt32(reader.GetValue(0)),
                    Convert.ToDecimal(reader.GetValue(1)),
                    Convert.ToDecimal(reader.GetValue(2))));
            }
        }

        return new LegacyCafeteriaMonthSnapshot(year, month, throughAt, transactions, accounts);
    }
}
