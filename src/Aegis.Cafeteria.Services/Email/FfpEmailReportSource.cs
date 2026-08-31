namespace Aegis.Cafeteria.Services.Email;

using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;
using System.Text.RegularExpressions;

public sealed record DailyLedgerSummary(int Transactions, decimal Purchases, decimal Refunds, decimal AdditionalCredit, decimal StipendReductions);
public sealed record EmployeeWeeklyLedger(int EmployeeId, string EmployeeName, string Email, IReadOnlyList<EmployeeWeeklyLedgerLine> Lines);
public sealed record EmployeeWeeklyLedgerLine(DateTime ScanDate, string IdName, string Description, decimal ScanValue);

public sealed class FfpEmailReportSource(FfpIntegrationOptions ffp, ScheduledEmailOptions email)
{
    public async Task<DailyLedgerSummary> GetDailySummaryAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        var from = date.ToDateTime(TimeOnly.MinValue);
        var to = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        const string sql = @"
SELECT
    COUNT(*) AS TxCount,
    SUM(CASE WHEN IDName='Cafeteria Purchase' THEN ISNULL(ScanValue,0) ELSE 0 END) AS Purchases,
    SUM(CASE WHEN IDName='Cafeteria Refund' THEN ABS(ISNULL(ScanValue,0)) ELSE 0 END) AS Refunds,
    SUM(CASE WHEN IDName='Additional Credit' THEN ISNULL(ScanValue,0) ELSE 0 END) AS AdditionalCredit,
    SUM(CASE WHEN IDName='Stipend Card Reduction' THEN ISNULL(ScanValue,0) ELSE 0 END) AS Stipends
FROM dbo.EmployeeIDScans
WHERE ScanDate >= @from AND ScanDate < @to
  AND IDName IN ('Cafeteria Purchase','Cafeteria Refund','Additional Credit','Stipend Card Reduction');";
        await using var cn = new SqlConnection(ffp.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@from", from); cmd.Parameters.AddWithValue("@to", to);
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await r.ReadAsync(cancellationToken)) return new(0,0m,0m,0m,0m);
        return new(Convert.ToInt32(r.GetValue(0)), Decimal(r,1), Decimal(r,2), Decimal(r,3), Decimal(r,4));
    }

    public async Task<IReadOnlyList<EmployeeWeeklyLedger>> GetWeeklyEmployeeLedgersAsync(DateOnly weekStart, DateOnly weekEnd, CancellationToken cancellationToken = default)
    {
        var column = ValidateColumn(email.EmployeeEmailColumn);
        var sql = $@"
SELECT s.EmployeeID,
       ISNULL(e.FirstName,'') + ' ' + ISNULL(e.LastName,'') AS EmployeeName,
       ISNULL(CONVERT(nvarchar(320), e.[{column}]), '') AS Email,
       s.ScanDate, ISNULL(s.IDName,''), ISNULL(s.[Description],''), ISNULL(s.ScanValue,0)
FROM dbo.EmployeeIDScans s
INNER JOIN dbo.Employees e ON e.ID=s.EmployeeID
WHERE s.ScanDate >= @from AND s.ScanDate < @to
  AND s.IDName IN ('Tickets Assigned','Cafeteria Purchase','Cafeteria Refund','Additional Credit','Stipend Card Reduction')
ORDER BY s.EmployeeID, s.ScanDate, s.ID;";
        await using var cn = new SqlConnection(ffp.ConnectionString);
        await cn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, cn);
        cmd.Parameters.AddWithValue("@from", weekStart.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@to", weekEnd.AddDays(1).ToDateTime(TimeOnly.MinValue));
        await using var r = await cmd.ExecuteReaderAsync(cancellationToken);
        var map = new Dictionary<int,(string Name,string Email,List<EmployeeWeeklyLedgerLine> Lines)>();
        while (await r.ReadAsync(cancellationToken))
        {
            var id=Convert.ToInt32(r.GetValue(0));
            if(!map.TryGetValue(id,out var x)) x=(Convert.ToString(r.GetValue(1))?.Trim()??string.Empty,Convert.ToString(r.GetValue(2))?.Trim()??string.Empty,[]);
            x.Lines.Add(new EmployeeWeeklyLedgerLine(r.GetDateTime(3),Convert.ToString(r.GetValue(4))??string.Empty,Convert.ToString(r.GetValue(5))??string.Empty,Convert.ToDecimal(r.GetValue(6))));
            map[id]=x;
        }
        return map.Where(x=>!string.IsNullOrWhiteSpace(x.Value.Email)).Select(x=>new EmployeeWeeklyLedger(x.Key,x.Value.Name,x.Value.Email,x.Value.Lines)).ToArray();
    }

    private static decimal Decimal(SqlDataReader r,int i)=>r.IsDBNull(i)?0m:Convert.ToDecimal(r.GetValue(i));
    private static string ValidateColumn(string value)
    {
        var v=string.IsNullOrWhiteSpace(value)?"Email":value.Trim();
        if(!Regex.IsMatch(v,"^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidOperationException("ScheduledEmail:EmployeeEmailColumn is not a valid SQL column identifier.");
        return v;
    }
}
