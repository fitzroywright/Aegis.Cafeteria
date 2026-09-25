using Microsoft.Data.SqlClient;
using System.Net.Http.Json;

MigrationOptions options = MigrationOptions.Parse(args);

Console.WriteLine($"Aegis.Cafeteria legacy migration - {options.Year:D4}-{options.Month:D2}");
Console.WriteLine($"Source: {options.Server} / {options.Database}");
Console.WriteLine($"Mode: {(options.Execute ? "EXECUTE" : "DRY RUN")}");

LegacyMonthMigrationRequest request = await ReadLegacyMonthAsync(options);

Console.WriteLine($"Through: {request.ThroughAt:O}");
Console.WriteLine($"Transactions: {request.Transactions.Count}");
Console.WriteLine($"Accounts: {request.Accounts.Count}");
Console.WriteLine($"Net scan value: {request.Transactions.Sum(x => x.ScanValue):N2}");
Console.WriteLine($"Account credit limit total: {request.Accounts.Sum(x => x.CreditLimit):N2}");
Console.WriteLine($"Account available balance total: {request.Accounts.Sum(x => x.AvailableBalance):N2}");

foreach (IGrouping<int, LegacyTransactionMigrationItem> group in request.Transactions
    .GroupBy(x => x.ScanType)
    .OrderBy(x => x.Key))
{
    Console.WriteLine($"ScanType {group.Key}: {group.Count()} transaction(s), {group.Sum(x => x.ScanValue):N2}");
}

if (!options.Execute)
{
    Console.WriteLine("Dry run complete. Re-run with --execute to submit the migration.");
    return;
}

using HttpClientHandler handler = new()
{
    UseDefaultCredentials = true
};
using HttpClient http = new(handler)
{
    BaseAddress = new Uri(options.ServicesUrl.TrimEnd('/') + "/"),
    Timeout = TimeSpan.FromMinutes(5)
};

using HttpResponseMessage response = await http.PostAsJsonAsync("api/migration/legacy-month", request);
string responseBody = await response.Content.ReadAsStringAsync();

if (!response.IsSuccessStatusCode)
{
    throw new InvalidOperationException(
        $"Migration endpoint returned HTTP {(int)response.StatusCode}: {responseBody}");
}

Console.WriteLine("Migration accepted by Cafeteria Services.");
Console.WriteLine(responseBody);

static async Task<LegacyMonthMigrationRequest> ReadLegacyMonthAsync(MigrationOptions options)
{
    DateTime start = new(options.Year, options.Month, 1);
    DateTime end = start.AddMonths(1);
    DateTimeOffset throughAt = DateTimeOffset.Now;

    const string transactionSql = @"
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
ORDER BY s.ScanDate, s.ID;";

    const string accountSql = @"
SELECT
      e.ID
    , ISNULL(e.CreditLimit, 0)
    , ISNULL(e.AvailableBalance, 0)
FROM dbo.Employees AS e
WHERE e.EligibleForLunchTicket = 1
ORDER BY e.ID;";

    SqlConnectionStringBuilder builder = new()
    {
        DataSource = options.Server,
        InitialCatalog = options.Database,
        IntegratedSecurity = true,
        Encrypt = options.Encrypt,
        TrustServerCertificate = options.TrustServerCertificate,
        ApplicationName = "Aegis.Cafeteria.Migration"
    };

    List<LegacyTransactionMigrationItem> transactions = [];
    List<LegacyAccountMigrationItem> accounts = [];

    await using SqlConnection connection = new(builder.ConnectionString);
    await connection.OpenAsync();

    await using (SqlCommand command = new(transactionSql, connection))
    {
        command.Parameters.AddWithValue("@Start", start);
        command.Parameters.AddWithValue("@End", end);
        command.Parameters.AddWithValue("@ThroughAt", throughAt.LocalDateTime);

        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            transactions.Add(new LegacyTransactionMigrationItem
            (
                  Convert.ToInt64(reader.GetValue(0))
                , Convert.ToInt32(reader.GetValue(1))
                , new DateTimeOffset(reader.GetDateTime(2))
                , Convert.ToString(reader.GetValue(3))?.Trim() ?? string.Empty
                , Convert.ToString(reader.GetValue(4))?.Trim() ?? string.Empty
                , Convert.ToDecimal(reader.GetValue(5))
                , Convert.ToString(reader.GetValue(6))?.Trim() ?? string.Empty
                , Convert.ToString(reader.GetValue(7))?.Trim() ?? string.Empty
                , Convert.ToInt32(reader.GetValue(8))
            ));
        }
    }

    await using (SqlCommand command = new(accountSql, connection))
    {
        await using SqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            accounts.Add(new LegacyAccountMigrationItem
            (
                  Convert.ToInt32(reader.GetValue(0))
                , Convert.ToDecimal(reader.GetValue(1))
                , Convert.ToDecimal(reader.GetValue(2))
            ));
        }
    }

    return new LegacyMonthMigrationRequest
    (
          options.Year
        , options.Month
        , throughAt
        , transactions
        , accounts
    );
}

internal sealed record LegacyMonthMigrationRequest
(
      int Year
    , int Month
    , DateTimeOffset ThroughAt
    , IReadOnlyList<LegacyTransactionMigrationItem> Transactions
    , IReadOnlyList<LegacyAccountMigrationItem> Accounts
);

internal sealed record LegacyTransactionMigrationItem
(
      long SourceId
    , int CustomerId
    , DateTimeOffset OccurredAt
    , string IdName
    , string Description
    , decimal ScanValue
    , string UserName
    , string ScanCode
    , int ScanType
);

internal sealed record LegacyAccountMigrationItem
(
      int CustomerId
    , decimal CreditLimit
    , decimal AvailableBalance
);

internal sealed record MigrationOptions
(
      string Server
    , string Database
    , string ServicesUrl
    , int Year
    , int Month
    , bool Execute
    , bool Encrypt
    , bool TrustServerCertificate
)
{
    internal static MigrationOptions Parse(string[] args)
    {
        Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index++)
        {
            string argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key = argument[2..];
            string? value = index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++index]
                : null;
            values[key] = value;
        }

        string server = Required(values, "server");
        string database = Required(values, "database");
        string servicesUrl = Required(values, "services-url");

        int year = values.TryGetValue("year", out string? yearText) && int.TryParse(yearText, out int parsedYear)
            ? parsedYear
            : DateTime.Today.Year;
        int month = values.TryGetValue("month", out string? monthText) && int.TryParse(monthText, out int parsedMonth)
            ? parsedMonth
            : DateTime.Today.Month;

        if (year is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        return new MigrationOptions
        (
              server
            , database
            , servicesUrl
            , year
            , month
            , values.ContainsKey("execute")
            , !values.ContainsKey("no-encrypt")
            , values.ContainsKey("trust-server-certificate")
        );
    }

    private static string Required(IReadOnlyDictionary<string, string?> values, string key)
        => values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : throw new ArgumentException($"Missing required --{key} value.");
}
