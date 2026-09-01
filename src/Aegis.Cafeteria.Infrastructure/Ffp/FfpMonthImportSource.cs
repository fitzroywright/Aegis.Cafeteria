namespace Aegis.Cafeteria.Infrastructure.Ffp;

using Aegis.Cafeteria.Application.Integration;
using Aegis.Cafeteria.Infrastructure.Configuration;
using Microsoft.Data.SqlClient;

/// <summary>
/// Service-side read-only source used during mid-month cutover. POS terminals never access FFP SQL.
/// </summary>
public sealed class FfpMonthImportSource(FfpIntegrationOptions options) : IFfpMonthImportSource
{
    public async Task<FfpMonthImportSnapshot> GetMonthAsync
    (
          int year
        , int month
        , CancellationToken cancellationToken = default
    )
    {
        if (year < 2000 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year));
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month));
        }

        if (!options.Enabled || string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("FFP integration is not configured in Aegis.Cafeteria.Services.");
        }

        DateTime start = new(year, month, 1);
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
  AND s.IDName IN
  (
        'Tickets Assigned'
      , 'Monthly Allocation Recalculation'
      , 'Cafeteria Purchase'
      , 'Cafeteria Refund'
      , 'Cafeteria Prior Period Refund'
      , 'Customer Payment'
      , 'Additional Credit'
      , 'Stipend Card Reduction'
  )
ORDER BY s.ScanDate, s.ID;";

        const string accountSql = @"
SELECT
      e.ID
    , ISNULL(e.CreditLimit, 0)
    , ISNULL(e.AvailableBalance, 0)
FROM dbo.Employees AS e
WHERE e.EligibleForLunchTicket = 1
ORDER BY e.ID;";

        List<ExternalFfpTransactionSnapshot> transactions = [];
        List<ExternalFfpAccountSnapshot> accounts = [];

        await using SqlConnection connection = new(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using (SqlCommand command = new(transactionSql, connection))
        {
            command.Parameters.AddWithValue("@Start", start);
            command.Parameters.AddWithValue("@End", end);
            command.Parameters.AddWithValue("@ThroughAt", throughAt.LocalDateTime);

            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                transactions.Add
                (
                    new ExternalFfpTransactionSnapshot
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
                    )
                );
            }
        }

        await using (SqlCommand command = new(accountSql, connection))
        {
            await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                accounts.Add
                (
                    new ExternalFfpAccountSnapshot
                    (
                          Convert.ToInt32(reader.GetValue(0))
                        , Convert.ToDecimal(reader.GetValue(1))
                        , Convert.ToDecimal(reader.GetValue(2))
                    )
                );
            }
        }

        return new FfpMonthImportSnapshot
        (
              year
            , month
            , throughAt
            , transactions
            , accounts
        );
    }
}
