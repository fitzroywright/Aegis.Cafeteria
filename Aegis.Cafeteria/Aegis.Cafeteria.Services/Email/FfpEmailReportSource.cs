namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Infrastructure.ActiveDirectory;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

public sealed record DailyLedgerSummary(int Transactions, decimal Purchases, decimal Refunds, decimal AdditionalCredit, decimal StipendReductions);
public sealed record EmployeeWeeklyLedger(int EmployeeId, string EmployeeName, string Email, IReadOnlyList<EmployeeWeeklyLedgerLine> Lines);
public sealed record EmployeeWeeklyLedgerLine(DateTime ScanDate, string IdName, string Description, decimal ScanValue);

public sealed class FfpEmailReportSource(FfpIntegrationOptions ffp, ScheduledEmailOptions email)
{
    private readonly ActiveDirectoryEmployeeDirectory directory = new();

    public async Task<DailyLedgerSummary> GetDailySummaryAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        DateTime from = date.ToDateTime(TimeOnly.MinValue);
        DateTime to = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        const string sql = @"
SELECT
    COUNT(*) AS TxCount,
    SUM(CASE WHEN IDName='Cafeteria Purchase' THEN ISNULL(ScanValue,0) ELSE 0 END) AS Purchases,
    SUM(CASE WHEN IDName IN ('Cafeteria Refund','Cafeteria Prior Period Refund') THEN ABS(ISNULL(ScanValue,0)) ELSE 0 END) AS Refunds,
    SUM(CASE WHEN IDName='Additional Credit' THEN ISNULL(ScanValue,0) ELSE 0 END) AS AdditionalCredit,
    SUM(CASE WHEN IDName='Stipend Card Reduction' THEN ISNULL(ScanValue,0) ELSE 0 END) AS Stipends
FROM dbo.EmployeeIDScans
WHERE ScanDate >= @from AND ScanDate < @to
  AND IDName IN ('Cafeteria Purchase','Cafeteria Refund','Cafeteria Prior Period Refund','Customer Payment','Additional Credit','Stipend Card Reduction');";

        await using SqlConnection connection = new(ffp.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@from", from);
        command.Parameters.AddWithValue("@to", to);
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new DailyLedgerSummary(0, 0m, 0m, 0m, 0m);
        }

        return new DailyLedgerSummary(
            Convert.ToInt32(reader.GetValue(0)),
            Decimal(reader, 1),
            Decimal(reader, 2),
            Decimal(reader, 3),
            Decimal(reader, 4));
    }

    public async Task<IReadOnlyList<EmployeeWeeklyLedger>> GetWeeklyEmployeeLedgersAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken cancellationToken = default)
    {
        const string sql = @"
SELECT s.EmployeeID,
       ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastName,'') AS EmployeeName,
       ISNULL(CONVERT(nvarchar(256), e.LoginName), '') AS LoginName,
       s.ScanDate,
       ISNULL(s.IDName,''),
       ISNULL(s.[Description],''),
       ISNULL(s.ScanValue,0)
FROM dbo.EmployeeIDScans s
INNER JOIN dbo.Employees e ON e.ID=s.EmployeeID
WHERE s.ScanDate >= @from AND s.ScanDate < @to
  AND s.IDName IN ('Tickets Assigned','Monthly Allocation Recalculation','Cafeteria Purchase','Cafeteria Refund','Cafeteria Prior Period Refund','Customer Payment','Additional Credit','Stipend Card Reduction')
ORDER BY s.EmployeeID, s.ScanDate, s.ID;";

        await using SqlConnection connection = new(ffp.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("@from", weekStart.ToDateTime(TimeOnly.MinValue));
        command.Parameters.AddWithValue("@to", weekEnd.AddDays(1).ToDateTime(TimeOnly.MinValue));
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        Dictionary<int, (string Name, string LoginName, List<EmployeeWeeklyLedgerLine> Lines)> map = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            int id = Convert.ToInt32(reader.GetValue(0));
            if (!map.TryGetValue(id, out (string Name, string LoginName, List<EmployeeWeeklyLedgerLine> Lines) current))
            {
                current = (
                    Convert.ToString(reader.GetValue(1))?.Trim() ?? string.Empty,
                    Convert.ToString(reader.GetValue(2))?.Trim() ?? string.Empty,
                    []);
            }

            current.Lines.Add(new EmployeeWeeklyLedgerLine(
                reader.GetDateTime(3),
                Convert.ToString(reader.GetValue(4)) ?? string.Empty,
                Convert.ToString(reader.GetValue(5)) ?? string.Empty,
                Convert.ToDecimal(reader.GetValue(6))));
            map[id] = current;
        }

        IReadOnlyDictionary<string, ActiveDirectoryEmployeeRecord> adRecords = directory.GetByLoginNames(
            map.Values.Select(value => value.LoginName));

        List<EmployeeWeeklyLedger> result = [];
        foreach (KeyValuePair<int, (string Name, string LoginName, List<EmployeeWeeklyLedgerLine> Lines)> item in map)
        {
            string loginName = NormalizeLoginName(item.Value.LoginName);
            if (loginName.Length == 0 || !adRecords.TryGetValue(loginName, out ActiveDirectoryEmployeeRecord? adEmployee))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(adEmployee.EmailAddress))
            {
                continue;
            }

            result.Add(new EmployeeWeeklyLedger(
                item.Key,
                item.Value.Name,
                adEmployee.EmailAddress,
                item.Value.Lines));
        }

        return result;
    }

    private static decimal Decimal(SqlDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));

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
}
